﻿using System;
using System.Collections.Generic;
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
            /*var linesOffset = */reader.ReadUInt32();
            var maskOffset = reader.ReadUInt32();
            var paletteOffset = reader.ReadUInt32();
            var properties = reader.ReadUInt32();

            var width = reader.ReadInt32();
            if (width != oldWidth)
                return data;

            var height = reader.ReadInt32();
            var centerX = reader.ReadInt32();
            var centerY = reader.ReadInt32();

            if (height - centerY != oldHeight)
                return data;

            if (centerY != 0)
            {
                int higher = newHeight - oldHeight;
                centerY -= higher;
                newHeight = oldHeight = height;
            }

            var newMaskSize = (uint) (newHeight*4);
            var newLinesOffset = maskOffset + newMaskSize;
            var newLinesSize = (uint) (newHeight*4);
            var newLineDataStart = newLinesOffset + newLinesSize;

            var outStream = new MemoryStream();
            var writer = new BinaryWriter(outStream);

            UserFeedback.Info(string.Format("Resizing image #{0} for {1}x{2} to {3}x{4}",
                id, oldWidth, oldHeight, newWidth, newHeight));

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

            var osp = outStream.Position;
            Trace.Assert(osp == maskOffset);

            var orgLineMasks = new UInt32[oldHeight];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                orgLineMasks[inLine] = reader.ReadUInt32();

            var orgLineStarts = new UInt32[oldHeight + 1];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                orgLineStarts[inLine] = reader.ReadUInt32();
            orgLineStarts[oldHeight] = (uint) data.Length;

            var orgLines = new List<byte[]>(oldHeight);
            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                var orgLineSize = orgLineStarts[inLine + 1] - orgLineStarts[inLine];
                orgLines.Add(reader.ReadBytes((int) orgLineSize));
            }

            var newLineMasks = new UInt32[newHeight];
            var newLines = new List<byte[]>(newHeight);

            var extraLines = newHeight - oldHeight;
            var shrinking = extraLines < 0;
            var expanding = extraLines > 0; 

            var centreLine = oldHeight / 2;

            var shrinkSkipStart = centreLine + extraLines;
            var shrinkSkipEnd = centreLine;

            // duplication is the amount of times every duplicated line, is duplicated
            var duplication = 1 + (extraLines + 1)/(oldHeight/2);
            // the duplication block size is the block that is being duplicated
            // the last line of which MIGHT not be duplicated 'duplication' times
            //  eg if duplication is 2, extralines = 501, dbs = 251, the last of which is
            //  duped just once.
            var duplicationBlockSize = (extraLines + duplication - 1)/duplication;

            var dupStart = centreLine - duplicationBlockSize / 2;
            var dupEnd = dupStart + duplicationBlockSize;

            var dupedLines = 0; //'UNIT'TEST
            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                // If shrinking skip 'extralines' lines on the centerline and above
                if (shrinking && inLine >= shrinkSkipStart && inLine < shrinkSkipEnd)
                {
                    dupedLines--;
                    continue;
                }

                newLineMasks[newLines.Count] = orgLineMasks[inLine];

                var newLine = StretchLine(orgLines[inLine], oldWidth, newWidth);
                newLines.Add(newLine);

                // If expanding, duplicate the right amount of lines before centreLine
                if (!expanding || inLine < dupStart || inLine >= dupEnd)
                    continue;

                for (var rep = 0; rep < duplication && dupedLines < extraLines; rep++)
                {
                    newLineMasks[newLines.Count] = orgLineMasks[inLine];
                    newLines.Add(newLine);
                    dupedLines++;
                }
            }
            Trace.Assert(newLines.Count == newHeight);
            Trace.Assert(dupedLines == extraLines);

            var nextLineStart = newLineDataStart;

            foreach (var newLineMask in newLineMasks)
                writer.Write(newLineMask);

            osp = outStream.Position;
            Trace.Assert(newLinesOffset == osp);

            foreach (var newLine in newLines)
            {
                writer.Write(nextLineStart);
                nextLineStart += (uint)newLine.Length;
            }

            osp = outStream.Position;
            Trace.Assert(newLineDataStart == osp);

            foreach (var newLine in newLines)
                writer.Write(newLine);

            Trace.Assert(outStream.Position == nextLineStart);

            writer.Close();

            var newSlp = outStream.ToArray();

            // For debugging:
            //File.WriteAllBytes(string.Format(@"slp\{0}org.slp", id), data);
            //File.WriteAllBytes(string.Format(@"slp\{0}new.slp", id), newSlp);

            return newSlp;
        }

        private static byte[] BlitColor(int amount, byte color)
        {
            if (amount <=0)
                return null;

            using (var outStream = new MemoryStream())
            {
                while (amount > 15)
                {
                    var count = Math.Min(amount, 255);
                    outStream.WriteByte(7);
                    outStream.WriteByte((byte) count);
                    outStream.WriteByte(color);
                    amount -= count;
                }
                while (amount > 0)
                {
                    var count = Math.Min(amount, 15);
                    outStream.WriteByte((byte) (7 | (count << 4)));
                    outStream.WriteByte(color);
                    amount -= count;
                }
                return outStream.ToArray();
            }
        }

        private static byte[] StretchLine( byte[] orgLine, int oldWidth, int newWidth)
        {
            if (orgLine.Length == 1)
                return orgLine;

            // Take the last byte before the 0x0F eol as color (I suppose that's always a color :S )
            var color = orgLine[orgLine.Length - 2];
            var addendum = BlitColor(newWidth - oldWidth, color);
            if (addendum == null || addendum.Length == 0)
                return orgLine;

            using (var outStream = new MemoryStream(orgLine.Length + addendum.Length))
            {
                outStream.Write(orgLine,0, orgLine.Length - 1);
                outStream.Write(addendum, 0, addendum.Length);
                outStream.WriteByte(orgLine[orgLine.Length - 1]);
                return outStream.ToArray();
            }
        }
    }
}
