using System.Buffers;
using System.CommandLine;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace EventStreamClient;

public class DecodeCommand : Command
{
    public DecodeCommand() : base("decode")
    {
        var url = DefaultOptions.Url;
        var queryPayload = DefaultOptions.QueryPayload;

        Add(url);
        Add(queryPayload);
        this.SetHandler(Handle, url, queryPayload);
    }

    private async Task Handle(string url, string queryPayload)
    {
        var client = SseClientFactory.Create(url);

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = JsonContent.Create(new
            {
                query = queryPayload
            }),
#if NETCOREAPP3_1_OR_GREATER
            Version = HttpVersion.Version20,
#endif
        };
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(input: "text/event-stream"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

#if NETCOREAPP3_1_OR_GREATER
        await using var stream = await response.Content.ReadAsStreamAsync();
#else
        using var stream = await response.Content.ReadAsStreamAsync();
#endif
        var reader = PipeReader.Create(stream);
        var parser = new EventStreamParser();

        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        using var _ = new Timer(_ =>
        {
            var elapsed = stopwatch.Elapsed;
            Console.WriteLine($"{count / elapsed.TotalSeconds:F1} events / sec | {elapsed.TotalSeconds:F0}s elapsed | total: {count}");
        }, null, 1000, 1000);

        while (true)
        {
            if (!reader.TryRead(out var result))
            {
                result = await reader.ReadAsync(default);
            }
            if (result.IsCompleted)
            {
                return;
            }

            if (parser.Parse(result, out var consumed, out var examined, out var eventStream))
            {
                count++;
                // Console.WriteLine("{0}: {1}", eventStream.EventType, eventStream.Data);
            }
            reader.AdvanceTo(consumed, examined);
        }
    }

    private class EventStreamParser
    {
        private const byte ByteCr = (byte)'\r';
        private const byte ByteLf = (byte)'\n';
        private static ReadOnlySpan<char> EventField => new[] { 'e', 'v', 'e', 'n', 't' };
        private static ReadOnlySpan<char> DataField => new[] { 'd', 'a', 't', 'a' };

        private string _eventType = string.Empty;
        private readonly StringBuilder _data = new();

        public bool Parse(
            in ReadResult result,
            out SequencePosition consumed,
            out SequencePosition examined,
            out EventStream? eventStream)
        {
            var dispatch = false;

            var lines = new LineEnumerator(result);

            while (lines.MoveNext())
            {
                var buffer = lines.Current;

                if (buffer.IsEmpty)
                {
                    dispatch = true;
                    break;
                }

#if NET5_0_OR_GREATER
                var line = Encoding.UTF8.GetString(buffer).AsSpan();
#else
                var line = Encoding.UTF8.GetString(buffer.ToArray()).AsSpan();
#endif

                var column = line.IndexOf(':');

                // Skip comments.
                if (column == 0)
                {
                    continue;
                }

                var fieldName = line.Slice(0, column);
                var fieldValue = ReadOnlySpan<char>.Empty;
                if (column > 0)
                {
                    fieldValue = line.Slice(column + 1);
                    if (fieldValue.StartsWith(new[] { ' ' }))
                    {
                        fieldValue = fieldValue.Slice(1);
                    }
                }

                if (fieldName.SequenceEqual(EventField))
                {
                    _eventType = fieldValue.ToString();
                }
                else if (fieldName.SequenceEqual(DataField))
                {
                    _data.AppendLine(fieldValue.ToString());
                }
            }

            if (!dispatch)
            {
                consumed = lines.Consumed;
                examined = lines.Examined;
                eventStream = null;
            }
            else
            {
                consumed = lines.Consumed;
                examined = consumed;
                if (_data.Length > 0)
                {
                    _data.Length--;
                }
                eventStream = new EventStream
                {
                    EventType = _eventType,
                    Data = _data.ToString(),
                };
                _eventType = string.Empty;
                _data.Clear();
            }

            return dispatch;
        }

        public class EventStream
        {
            public string EventType { get; set; } = string.Empty;
            public string Data { get; set; } = string.Empty;
        }

        private ref struct LineEnumerator
        {
            private readonly bool _isCompleted;
            private ReadOnlySequence<byte> _buffer;
            private SequencePosition _trimmedLineEnd;
            private SequencePosition _lineEnd;

            public LineEnumerator(in ReadResult result)
            {
                _isCompleted = result.IsCompleted;
                _buffer = result.Buffer;
                _trimmedLineEnd = _buffer.Start;
                _lineEnd = _buffer.Start;
            }

            public ReadOnlySequence<byte> Current => _buffer.Slice(0, _trimmedLineEnd);
            public SequencePosition Consumed => _lineEnd;
            public SequencePosition Examined => _buffer.End;

            public bool MoveNext()
            {
                if (!_buffer.Start.Equals(_lineEnd))
                {
                    _buffer = _buffer.Slice(_lineEnd);
                }

                if (_buffer.IsEmpty)
                {
                    return false;
                }

                _trimmedLineEnd = GetLineEnd(_buffer);

                var isLine = !_trimmedLineEnd.Equals(_buffer.End);

                // The stream does not have a line ending. We need to read more.
                if (!isLine || !TryGetByte(_buffer, _trimmedLineEnd, out var lineEnding))
                {
                    return false;
                }

                if (lineEnding == ByteCr)
                {
                    var lfPosition = _buffer.GetPosition(1, _trimmedLineEnd);
                    var endOfStream = _isCompleted && lfPosition.Equals(_buffer.End);

                    // Need 1 more byte to make a CRLF pair, except the end of stream.
                    if (!TryGetByte(_buffer, lfPosition, out var lf)
                        && !endOfStream)
                    {
                        // _buffer = _buffer.Slice(0, lfPosition);
                        return false;
                    }

                    _lineEnd = lf != ByteLf
                        ? _buffer.GetPosition(1, _trimmedLineEnd)
                        : _buffer.GetPosition(2, _trimmedLineEnd);
                }
                else
                {
                    _lineEnd = _buffer.GetPosition(1, _trimmedLineEnd);
                }

                return true;
            }
        }

        private static SequencePosition GetLineEnd(in ReadOnlySequence<byte> buffer)
        {
            var offset = 0;

            foreach (var segment in buffer)
            {
                if (segment.Span.IndexOfAny(ByteCr, ByteLf) is var lineEnd && lineEnd != -1)
                {
                    return buffer.GetPosition(offset + lineEnd);
                }

                offset += segment.Length;
            }

            return buffer.End;
        }

        private static bool TryGetByte(in ReadOnlySequence<byte> buffer, SequencePosition position, out byte item)
        {
            if (buffer.TryGet(ref position, out var memory, false)
                && !memory.IsEmpty)
            {
                item = memory.Span[0];
                return true;
            }

            item = 0;
            return false;
        }
    }
}