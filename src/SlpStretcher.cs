using System;
using System.Diagnostics;
using System.IO;

namespace AoE2Wide
{
    internal static class SlpStretcher
    {
        public static byte[] Enlarge(uint id, byte[] data, int oldWidth, int oldHeight, int newWidth, int newHeight)
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

            var width = reader.ReadInt32();
            if (width != oldWidth)
                return data;

            var height = reader.ReadInt32();
            if (height != oldHeight)
                return data;

            var centerX = reader.ReadInt32();
            var centerY = reader.ReadInt32();

            var newMaskSize = (uint)(newHeight * 4);
            var newLinesOffset = maskOffset + newMaskSize;
            var newLinesSize = (uint)(newHeight * 4);
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
            writer.Write((Int32)newWidth);
            writer.Write((Int32)newHeight);
            writer.Write(centerX);
            writer.Write(centerY);

            var osp = outStream.Position;
            Trace.Assert(osp == maskOffset);

            var extraLines = newHeight - oldHeight;

            var centreLine = oldHeight/2;
            if (extraLines >= 0)
            {
                for (var inLine = 0; inLine < oldHeight; inLine++)
                {
                    var lineMaskData = reader.ReadUInt32();
                    writer.Write(lineMaskData);
                    if (inLine != centreLine)
                        continue;

                    for (var el = 0; el < extraLines; el++)
                        writer.Write(lineMaskData);
                }
            }
            else
            {
                for (var inLine = 0; inLine < oldHeight; inLine++)
                {
                    var lineMaskData = reader.ReadUInt32();

                    // Skip 'extralines' lines on the centerline and above
                    if (inLine >= centreLine + extraLines && inLine < centreLine)
                        continue;

                    writer.Write(lineMaskData);
                }
            }

            osp = outStream.Position;
            Trace.Assert(osp == newLinesOffset);

            var lineStarts = new UInt32[oldHeight + 1];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                lineStarts[inLine] = reader.ReadUInt32();
            lineStarts[oldHeight] = (uint)data.Length;

            var lineSizes = new UInt32[oldHeight];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                lineSizes[inLine] = lineStarts[inLine + 1] - lineStarts[inLine];

            var nextLineStart = newLineDataStart;

            if (extraLines >= 0)
            {
                for (var inLine = 0; inLine < oldHeight; inLine++)
                {
                    writer.Write(nextLineStart);
                    nextLineStart += lineSizes[inLine];

                    if (inLine != centreLine)
                        continue;

                    for (var el = 0; el < extraLines; el++)
                    {
                        writer.Write(nextLineStart);
                        nextLineStart += lineSizes[inLine];
                    }
                }
            }
            else
            {
                for (var inLine = 0; inLine < oldHeight; inLine++)
                {
                    // Skip 'extralines' lines on the centerline and above
                    if (inLine >= centreLine + extraLines && inLine < centreLine)
                        continue;

                    writer.Write(nextLineStart);
                    nextLineStart += lineSizes[inLine];
                }
            }

            osp = outStream.Position;
            Trace.Assert(newLineDataStart == osp);

            if (extraLines >= 0)
            {
                for (var inLine = 0; inLine < oldHeight; inLine++)
                {
                    var lineData = reader.ReadBytes((int) lineSizes[inLine]);
                    writer.Write(lineData);

                    if (inLine != centreLine)
                        continue;

                    for (var el = 0; el < extraLines; el++)
                        writer.Write(lineData);
                }
            }
            else
            {
                for (var inLine = 0; inLine < oldHeight; inLine++)
                {
                    var lineData = reader.ReadBytes((int)lineSizes[inLine]);

                    // Skip 'extralines' lines on the centerline and above
                    if (inLine >= centreLine + extraLines && inLine < centreLine)
                        continue;

                    writer.Write(lineData);
                }
            }

            Trace.Assert(outStream.Position == nextLineStart);

            writer.Close();

            var newSlp = outStream.ToArray();

            // For debugging:
            //File.WriteAllBytes(string.Format(@"slp\{0}org.slp", id), data);
            //File.WriteAllBytes(string.Format(@"slp\{0}new.slp", id), newSlp);

            return newSlp;
        }
    }
}
