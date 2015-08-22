﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Server
{
    public class ByteBuffer
    {
        private int _currentLength;
        private readonly int? _maxLength;
        private readonly List<byte[]> _segments = new List<byte[]>();

        public ByteBuffer(int? maxLength)
        {
            _maxLength = maxLength;
        }

        public void Append(byte[] segment)
        {
            checked { _currentLength += segment.Length; }
            if (_maxLength.HasValue && _currentLength > _maxLength)
            {
                throw new InvalidOperationException("Buffer length exceeded");
            }

            _segments.Add(segment);
        }

        // returns the segments as a single byte array
        public byte[] GetByteArray()
        {
            var newArray = new byte[_currentLength];
            var lastOffset = 0;

            for (var i = 0; i < _segments.Count; i++)
            {
                var thisSegment = _segments[i];
                Buffer.BlockCopy(thisSegment, 0, newArray, lastOffset, thisSegment.Length);
                lastOffset += thisSegment.Length;
            }

            return newArray;
        }

        // treats the segments as UTF8-encoded information and returns the resulting string
        public string GetString()
        {
            var builder = new StringBuilder();
            var decoder = Encoding.UTF8.GetDecoder();

            for (var i = 0; i < _segments.Count; i++)
            {
                var flush = (i == _segments.Count - 1);
                var thisSegment = _segments[i];
                var charsRequired = decoder.GetCharCount(thisSegment, 0, thisSegment.Length, flush);
                var thisSegmentAsChars = new char[charsRequired];
                var numCharsConverted = decoder.GetChars(thisSegment, 0, thisSegment.Length, thisSegmentAsChars, 0, flush);
                builder.Append(thisSegmentAsChars, 0, numCharsConverted);
            }

            return builder.ToString();
        }
    }
}
