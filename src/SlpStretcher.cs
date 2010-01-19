using System;
using System.Diagnostics;
using System.IO;

namespace AoE2Wide
{
    internal static class SlpStretcher
    {
        public static byte[] Enlarge(byte[] data, uint oldWidth, uint oldHeight, uint newWidth, uint newHeight)
        {
            var reader = new BinaryReader(new MemoryStream(data, false));

            var version = reader.ReadUInt32();
            if (version != 0x4e302e32)
                return data;

            var framecount = reader.ReadUInt32();
            if (framecount != 1)
                return data;

            var comment = reader.ReadBytes(24);
            var linesOffset = reader.ReadUInt32();
            var maskOffset = reader.ReadUInt32();
            var paletteOffset = reader.ReadUInt32();
            var properties = reader.ReadUInt32();

            var width = reader.ReadUInt32();
            if (width != oldWidth)
                return data;

            var height = reader.ReadUInt32();
            if (height != oldHeight)
                return data;

            var centerX = reader.ReadInt32();
            var centerY = reader.ReadInt32();

            var newMaskSize = newHeight * 4;
            var newLinesOffset = maskOffset + newMaskSize;
            var newLinesSize = newHeight * 4;
            var newLineDataStart = newLinesOffset + newLinesSize;

            var outStream = new MemoryStream();
            var writer = new BinaryWriter(outStream);

            writer.Write(version);
            writer.Write(framecount);
            writer.Write(comment);
            writer.Write(newLinesOffset);
            writer.Write(maskOffset);
            writer.Write(paletteOffset);
            writer.Write(properties);
            writer.Write(newWidth);
            writer.Write(newHeight);
            writer.Write(centerX);
            writer.Write(centerY);

            var extraLines = newHeight - oldHeight;

            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                var lineMaskData = reader.ReadUInt32();
                writer.Write(lineMaskData);
                if (inLine != oldHeight / 2)
                    continue;

                for (var el = 0; el < extraLines; el++)
                    writer.Write(lineMaskData);
            }

            var lineStarts = new UInt32[oldHeight + 1];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                lineStarts[inLine] = reader.ReadUInt32();
            lineStarts[oldHeight] = (uint)data.Length;

            var lineSizes = new UInt32[oldHeight];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                lineSizes[inLine] = lineStarts[inLine + 1] - lineStarts[inLine];

            var nextLineStart = newLineDataStart;

            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                writer.Write(nextLineStart);
                nextLineStart += lineSizes[inLine];

                if (inLine != oldHeight / 2)
                    continue;

                for (var el = 0; el < extraLines; el++)
                {
                    writer.Write(nextLineStart);
                    nextLineStart += lineSizes[inLine];
                }
            }

            var osp = outStream.Position;
            Trace.Assert(newLineDataStart == osp);

            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                var lineData = reader.ReadBytes((int)lineSizes[inLine]);
                writer.Write(lineData);

                if (inLine != oldHeight / 2)
                    continue;

                for (var el = 0; el < extraLines; el++)
                    writer.Write(lineData);
            }

            writer.Close();

            return outStream.GetBuffer();
        }
    }
}
