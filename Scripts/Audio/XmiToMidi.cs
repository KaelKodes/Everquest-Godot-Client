using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Converts EverQuest XMI (Extended MIDI / Miles Sound System) files
/// to standard MIDI format that MeltySynth can play.
///
/// XMI uses IFF container format:
///   FORM/XDIR → INFO (song count) → CAT/XMID → FORM/XMID → TIMB + EVNT
///
/// Key differences from standard MIDI:
///   - Delta times are SUMMED (not variable-length encoded)
///   - High-bit set = MIDI event, high-bit clear = delay value
///   - Note On events include embedded duration (no explicit Note Off)
///   - Fixed clock rate of 120 Hz (PPQN = 60)
/// </summary>
public static class XmiToMidi
{
    /// <summary>
    /// Convert an XMI file to standard MIDI format in memory.
    /// Returns the MIDI byte array, or null on failure.
    /// </summary>
    public static byte[] Convert(byte[] xmiData)
    {
        if (xmiData == null || xmiData.Length < 20) return null;

        try
        {
            // Validate IFF header: "FORM" + size + "XDIR"
            if (ReadTag(xmiData, 0) != "FORM" || ReadTag(xmiData, 8) != "XDIR")
                return null;

            // Find the EVNT chunk (contains the actual MIDI event data)
            int evntOffset = FindChunk(xmiData, "EVNT", 0);
            if (evntOffset < 0) return null;

            int evntSize = ReadBE32(xmiData, evntOffset + 4);
            int evntStart = evntOffset + 8;

            // Parse XMI events into standard MIDI events
            var midiEvents = ParseXmiEvents(xmiData, evntStart, evntSize);
            if (midiEvents.Count == 0) return null;

            // Build standard MIDI file (format 0, single track)
            return BuildMidiFile(midiEvents);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Convert an XMI file from disk to standard MIDI bytes.
    /// </summary>
    public static byte[] ConvertFile(string xmiPath)
    {
        if (!File.Exists(xmiPath)) return null;
        byte[] data = File.ReadAllBytes(xmiPath);
        return Convert(data);
    }

    // ── XMI Event Parser ─────────────────────────────────────────

    private struct MidiEvent
    {
        public int DeltaTicks;
        public byte[] Data; // Raw MIDI event bytes (status + data)
    }

    private static List<MidiEvent> ParseXmiEvents(byte[] data, int offset, int length)
    {
        var events = new List<MidiEvent>();
        // Track pending Note Off events (channel → list of (tick, note))
        var pendingNoteOffs = new SortedList<int, List<(byte channel, byte note)>>();
        int end = offset + length;
        int pos = offset;
        int currentTick = 0;

        while (pos < end)
        {
            // Read delay: sum all bytes with high bit CLEAR
            int delay = 0;
            while (pos < end && (data[pos] & 0x80) == 0)
            {
                delay += data[pos];
                pos++;
            }

            currentTick += delay;

            // Insert any pending Note Off events that should fire before or at this tick
            InsertPendingNoteOffs(events, pendingNoteOffs, currentTick);

            if (pos >= end) break;

            byte status = data[pos];

            // Meta event (0xFF)
            if (status == 0xFF)
            {
                pos++;
                if (pos >= end) break;
                byte metaType = data[pos]; pos++;
                int metaLen = ReadVarLen(data, ref pos);

                var metaData = new byte[2 + GetVarLenSize(metaLen) + metaLen];
                int mdi = 0;
                metaData[mdi++] = 0xFF;
                metaData[mdi++] = metaType;
                WriteVarLen(metaData, ref mdi, metaLen);
                if (metaLen > 0 && pos + metaLen <= end)
                {
                    Array.Copy(data, pos, metaData, mdi, metaLen);
                }

                events.Add(new MidiEvent { DeltaTicks = delay, Data = metaData });
                pos += metaLen;

                // End of track
                if (metaType == 0x2F) break;
            }
            // SysEx (0xF0 or 0xF7)
            else if (status == 0xF0 || status == 0xF7)
            {
                pos++;
                int sysLen = ReadVarLen(data, ref pos);
                pos += sysLen; // Skip SysEx data
            }
            // Note On with embedded duration (XMI-specific)
            else if ((status & 0xF0) == 0x90)
            {
                pos++;
                if (pos + 1 >= end) break;
                byte note = data[pos++];
                byte velocity = data[pos++];

                // Read embedded duration (XMI variable-length)
                int duration = ReadVarLen(data, ref pos);

                // Emit Note On
                events.Add(new MidiEvent
                {
                    DeltaTicks = delay,
                    Data = new byte[] { status, note, velocity }
                });

                // Schedule Note Off
                int offTick = currentTick + duration;
                if (!pendingNoteOffs.ContainsKey(offTick))
                    pendingNoteOffs[offTick] = new List<(byte, byte)>();
                pendingNoteOffs[offTick].Add(((byte)(status & 0x0F), note));
            }
            // Other channel messages (2 data bytes: Note Off, Key Pressure, Control Change, Pitch Bend)
            else if ((status & 0xF0) == 0x80 || (status & 0xF0) == 0xA0 ||
                     (status & 0xF0) == 0xB0 || (status & 0xF0) == 0xE0)
            {
                pos++;
                if (pos + 1 >= end) break;
                byte d1 = data[pos++];
                byte d2 = data[pos++];
                events.Add(new MidiEvent
                {
                    DeltaTicks = delay,
                    Data = new byte[] { status, d1, d2 }
                });
            }
            // Program Change, Channel Pressure (1 data byte)
            else if ((status & 0xF0) == 0xC0 || (status & 0xF0) == 0xD0)
            {
                pos++;
                if (pos >= end) break;
                byte d1 = data[pos++];
                events.Add(new MidiEvent
                {
                    DeltaTicks = delay,
                    Data = new byte[] { status, d1 }
                });
            }
            else
            {
                // Unknown byte, skip
                pos++;
            }
        }

        // Flush remaining Note Offs
        InsertPendingNoteOffs(events, pendingNoteOffs, int.MaxValue);

        // Add End of Track if not already present
        if (events.Count == 0 || events[events.Count - 1].Data[0] != 0xFF ||
            events[events.Count - 1].Data[1] != 0x2F)
        {
            events.Add(new MidiEvent
            {
                DeltaTicks = 0,
                Data = new byte[] { 0xFF, 0x2F, 0x00 }
            });
        }

        return events;
    }

    private static void InsertPendingNoteOffs(List<MidiEvent> events,
        SortedList<int, List<(byte channel, byte note)>> pending, int upToTick)
    {
        var toRemove = new List<int>();
        foreach (var kvp in pending)
        {
            if (kvp.Key > upToTick) break;
            foreach (var (channel, note) in kvp.Value)
            {
                events.Add(new MidiEvent
                {
                    DeltaTicks = 0, // Delta will be recalculated in BuildMidiFile
                    Data = new byte[] { (byte)(0x80 | channel), note, 0x40 }
                });
            }
            toRemove.Add(kvp.Key);
        }
        foreach (int k in toRemove)
            pending.Remove(k);
    }

    // ── MIDI File Builder ────────────────────────────────────────

    private static byte[] BuildMidiFile(List<MidiEvent> events)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // MThd header
        bw.Write(new byte[] { 0x4D, 0x54, 0x68, 0x64 }); // "MThd"
        WriteBE32(bw, 6); // Header length
        WriteBE16(bw, 0); // Format 0 (single track)
        WriteBE16(bw, 1); // 1 track
        WriteBE16(bw, 60); // 60 PPQN (XMI standard: 120 Hz clock / 2)

        // MTrk header (placeholder, will fill in size later)
        bw.Write(new byte[] { 0x4D, 0x54, 0x72, 0x6B }); // "MTrk"
        long trackSizePos = ms.Position;
        WriteBE32(bw, 0); // Placeholder

        long trackStart = ms.Position;

        // Write all events with variable-length delta times
        foreach (var evt in events)
        {
            WriteVarLenToStream(bw, evt.DeltaTicks);
            bw.Write(evt.Data);
        }

        long trackEnd = ms.Position;
        int trackSize = (int)(trackEnd - trackStart);

        // Go back and fill in the track size
        ms.Position = trackSizePos;
        WriteBE32(bw, trackSize);

        return ms.ToArray();
    }

    // ── IFF / Binary Helpers ─────────────────────────────────────

    private static string ReadTag(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return "";
        return System.Text.Encoding.ASCII.GetString(data, offset, 4);
    }

    private static int ReadBE32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3];
    }

    private static int FindChunk(byte[] data, string tag, int startOffset)
    {
        for (int i = startOffset; i < data.Length - 8; i++)
        {
            if (ReadTag(data, i) == tag)
                return i;
        }
        return -1;
    }

    private static int ReadVarLen(byte[] data, ref int pos)
    {
        int value = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return value;
    }

    private static int GetVarLenSize(int value)
    {
        if (value < 0x80) return 1;
        if (value < 0x4000) return 2;
        if (value < 0x200000) return 3;
        return 4;
    }

    private static void WriteVarLen(byte[] buf, ref int pos, int value)
    {
        var bytes = new List<byte>();
        bytes.Add((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            bytes.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        bytes.Reverse();
        foreach (byte b in bytes)
            buf[pos++] = b;
    }

    private static void WriteVarLenToStream(BinaryWriter bw, int value)
    {
        var bytes = new List<byte>();
        bytes.Add((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            bytes.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        bytes.Reverse();
        bw.Write(bytes.ToArray());
    }

    private static void WriteBE32(BinaryWriter bw, int value)
    {
        bw.Write((byte)((value >> 24) & 0xFF));
        bw.Write((byte)((value >> 16) & 0xFF));
        bw.Write((byte)((value >> 8) & 0xFF));
        bw.Write((byte)(value & 0xFF));
    }

    private static void WriteBE16(BinaryWriter bw, int value)
    {
        bw.Write((byte)((value >> 8) & 0xFF));
        bw.Write((byte)(value & 0xFF));
    }
}
