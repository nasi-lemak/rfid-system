using System.Buffers.Binary;

namespace Rfid.Application.Llrp;

/// <summary>LLRP 1.0.1 (EPCglobal Low Level Reader Protocol) message types used by the client.</summary>
public static class LlrpMsg
{
    public const ushort SetReaderConfig = 3, SetReaderConfigResponse = 13, AddRoSpec = 20, DeleteRoSpec = 21, StartRoSpec = 22, StopRoSpec = 23, EnableRoSpec = 24,
        AddRoSpecResponse = 30, DeleteRoSpecResponse = 31, StartRoSpecResponse = 32, EnableRoSpecResponse = 34, RoAccessReport = 61, Keepalive = 62,
        ReaderEventNotification = 63, KeepaliveAck = 72, CloseConnection = 14, CloseConnectionResponse = 4, GetReaderCapabilities = 1;
}

public static class LlrpParam
{
    public const ushort RoSpec = 177, RoBoundarySpec = 178, RoSpecStartTrigger = 179, RoSpecStopTrigger = 182, AiSpec = 183, AiSpecStopTrigger = 184, InventoryParameterSpec = 186,
        KeepaliveSpec = 220, RoReportSpec = 237, TagReportContentSelector = 238, TagReportData = 240, EpcData = 241, ReaderEventNotificationData = 246, ConnectionAttemptEvent = 256, LlrpStatus = 287;
    // TV (type-value, 1-byte header) parameter types inside TagReportData and their fixed value lengths.
    public static readonly Dictionary<byte, int> TvLengths = new() { [1] = 2, [2] = 8, [3] = 8, [4] = 8, [5] = 8, [6] = 1, [7] = 2, [8] = 2, [9] = 4, [10] = 2, [11] = 2, [12] = 2, [13] = 12, [14] = 2, [15] = 4, [16] = 4, [17] = 2, [18] = 4, [19] = 2, [20] = 2 };
    public const byte TvAntennaId = 1, TvFirstSeenUtc = 2, TvLastSeenUtc = 4, TvPeakRssi = 6, TvChannelIndex = 7, TvTagSeenCount = 8, TvRoSpecId = 9, TvEpc96 = 13;
}

public record LlrpMessage(ushort Type, uint Id, byte[] Body);
public record LlrpTag(string Epc, int? AntennaId, double? PeakRssi, DateTime? FirstSeenUtc, DateTime? LastSeenUtc, int? SeenCount, int? ChannelIndex);
public record LlrpTlv(ushort Type, byte[] Value);

/// <summary>Binary encoder/decoder for the LLRP subset needed to run an inventory and receive tag reports.</summary>
public static class LlrpCodec
{
    public const int HeaderLength = 10;

    public static byte[] EncodeMessage(ushort type, uint id, params byte[][] body)
    {
        var len = HeaderLength + body.Sum(b => b.Length);
        var buf = new byte[len];
        BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)((1 << 10) | (type & 0x3FF))); // version 1
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(2), (uint)len);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(6), id);
        var o = HeaderLength; foreach (var b in body) { b.CopyTo(buf, o); o += b.Length; }
        return buf;
    }

    /// <summary>Returns (type, length, id) from a 10-byte header.</summary>
    public static (ushort type, int length, uint id) DecodeHeader(ReadOnlySpan<byte> h)
        => ((ushort)(BinaryPrimitives.ReadUInt16BigEndian(h) & 0x3FF), (int)BinaryPrimitives.ReadUInt32BigEndian(h[2..]), BinaryPrimitives.ReadUInt32BigEndian(h[6..]));

    public static byte[] Tlv(ushort type, params byte[][] parts)
    {
        var len = 4 + parts.Sum(p => p.Length);
        var buf = new byte[len];
        BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)(type & 0x3FF));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), (ushort)len);
        var o = 4; foreach (var p in parts) { p.CopyTo(buf, o); o += p.Length; }
        return buf;
    }
    public static byte[] U8(byte v) => new[] { v };
    public static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
    public static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }

    /// <summary>Parses the top-level TLV parameters of a message body (TV parameters only occur nested inside TagReportData).</summary>
    public static List<LlrpTlv> ParseTlvs(ReadOnlySpan<byte> body)
    {
        var list = new List<LlrpTlv>();
        var o = 0;
        while (o + 4 <= body.Length)
        {
            if ((body[o] & 0x80) != 0) { var t = (byte)(body[o] & 0x7F); var l = LlrpParam.TvLengths.GetValueOrDefault(t, 0); list.Add(new LlrpTlv(t, body.Slice(o + 1, Math.Min(l, body.Length - o - 1)).ToArray())); o += 1 + l; continue; }
            var type = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(body[o..]) & 0x3FF);
            var len = BinaryPrimitives.ReadUInt16BigEndian(body[(o + 2)..]);
            if (len < 4 || o + len > body.Length) break;
            list.Add(new LlrpTlv(type, body.Slice(o + 4, len - 4).ToArray()));
            o += len;
        }
        return list;
    }

    // ── Outbound messages ──────────────────────────────────────────────────────────────────────────

    /// <summary>SET_READER_CONFIG: reset to factory defaults + periodic keepalive.</summary>
    public static byte[] SetReaderConfig(uint id, int keepaliveMs = 10000)
        => EncodeMessage(LlrpMsg.SetReaderConfig, id, U8(0x80), Tlv(LlrpParam.KeepaliveSpec, U8(1), U32((uint)keepaliveMs)));

    public static byte[] DeleteRoSpec(uint id, uint roSpecId = 0) => EncodeMessage(LlrpMsg.DeleteRoSpec, id, U32(roSpecId));
    public static byte[] EnableRoSpec(uint id, uint roSpecId) => EncodeMessage(LlrpMsg.EnableRoSpec, id, U32(roSpecId));
    public static byte[] StartRoSpec(uint id, uint roSpecId) => EncodeMessage(LlrpMsg.StartRoSpec, id, U32(roSpecId));
    public static byte[] StopRoSpec(uint id, uint roSpecId) => EncodeMessage(LlrpMsg.StopRoSpec, id, U32(roSpecId));
    public static byte[] KeepaliveAck(uint id) => EncodeMessage(LlrpMsg.KeepaliveAck, id);
    public static byte[] CloseConnection(uint id) => EncodeMessage(LlrpMsg.CloseConnection, id);

    /// <summary>ADD_ROSPEC for a continuous Gen2 inventory on all antennas, reporting every N tags (N=1 → immediate).</summary>
    public static byte[] AddRoSpec(uint id, uint roSpecId, ushort reportEveryNTags = 1, ushort[]? antennaIds = null)
    {
        var ants = antennaIds ?? new ushort[] { 0 };
        var antBytes = new List<byte[]> { U16((ushort)ants.Length) }; antBytes.AddRange(ants.Select(U16));
        var aiSpec = Tlv(LlrpParam.AiSpec, antBytes.Concat(new[]
        {
            Tlv(LlrpParam.AiSpecStopTrigger, U8(0), U32(0)),                       // null trigger: run until ROSpec stops
            Tlv(LlrpParam.InventoryParameterSpec, U16(1), U8(1)),                   // spec id 1, EPCGlobal C1G2
        }).ToArray());
        var boundary = Tlv(LlrpParam.RoBoundarySpec, Tlv(LlrpParam.RoSpecStartTrigger, U8(0)), Tlv(LlrpParam.RoSpecStopTrigger, U8(0), U32(0)));
        // Content selector bits (MSB first): ROSpecID, SpecIndex, InvParamSpecID, AntennaID, ChannelIndex, PeakRSSI, FirstSeen, LastSeen, TagSeenCount, AccessSpecID
        const ushort selector = (1 << 15) | (1 << 12) | (1 << 11) | (1 << 10) | (1 << 9) | (1 << 8) | (1 << 7);
        var report = Tlv(LlrpParam.RoReportSpec, U8(1), U16(reportEveryNTags), Tlv(LlrpParam.TagReportContentSelector, U16(selector)));
        var roSpec = Tlv(LlrpParam.RoSpec, U32(roSpecId), U8(0), U8(0), boundary, aiSpec, report);
        return EncodeMessage(LlrpMsg.AddRoSpec, id, roSpec);
    }

    // ── Inbound parsing ────────────────────────────────────────────────────────────────────────────

    public static List<LlrpTag> ParseRoAccessReport(ReadOnlySpan<byte> body)
    {
        var tags = new List<LlrpTag>();
        foreach (var p in ParseTlvs(body).Where(p => p.Type == LlrpParam.TagReportData))
        {
            string? epc = null; int? ant = null, count = null, channel = null; double? rssi = null; DateTime? first = null, last = null;
            foreach (var f in ParseTlvs(p.Value))
            {
                switch (f.Type)
                {
                    case LlrpParam.TvEpc96: epc = Convert.ToHexString(f.Value); break;
                    case LlrpParam.EpcData: { var bits = BinaryPrimitives.ReadUInt16BigEndian(f.Value); epc = Convert.ToHexString(f.Value.AsSpan(2, Math.Min((bits + 7) / 8, f.Value.Length - 2))); break; }
                    case LlrpParam.TvAntennaId: ant = BinaryPrimitives.ReadUInt16BigEndian(f.Value); break;
                    case LlrpParam.TvPeakRssi: rssi = (sbyte)f.Value[0]; break;
                    case LlrpParam.TvFirstSeenUtc: first = DateTime.UnixEpoch.AddTicks((long)(BinaryPrimitives.ReadUInt64BigEndian(f.Value) * 10)); break;
                    case LlrpParam.TvLastSeenUtc: last = DateTime.UnixEpoch.AddTicks((long)(BinaryPrimitives.ReadUInt64BigEndian(f.Value) * 10)); break;
                    case LlrpParam.TvTagSeenCount: count = BinaryPrimitives.ReadUInt16BigEndian(f.Value); break;
                    case LlrpParam.TvChannelIndex: channel = BinaryPrimitives.ReadUInt16BigEndian(f.Value); break;
                }
            }
            if (epc != null) tags.Add(new LlrpTag(epc, ant, rssi, first, last, count, channel));
        }
        return tags;
    }

    /// <summary>Returns (statusCode, description) from an LLRPStatus parameter in a response body; 0 = success.</summary>
    public static (int code, string description) ParseStatus(ReadOnlySpan<byte> body)
    {
        var st = ParseTlvs(body).FirstOrDefault(p => p.Type == LlrpParam.LlrpStatus);
        if (st == null) return (-1, "no LLRPStatus");
        var code = BinaryPrimitives.ReadUInt16BigEndian(st.Value);
        var len = st.Value.Length >= 4 ? BinaryPrimitives.ReadUInt16BigEndian(st.Value.AsSpan(2)) : 0;
        var desc = len > 0 && st.Value.Length >= 4 + len ? System.Text.Encoding.UTF8.GetString(st.Value, 4, len) : "";
        return (code, desc);
    }

    public static byte[] Status(int code, string description = "")
    {
        var d = System.Text.Encoding.UTF8.GetBytes(description);
        return Tlv(LlrpParam.LlrpStatus, U16((ushort)code), U16((ushort)d.Length), d);
    }

    /// <summary>Builds a RO_ACCESS_REPORT (used by the in-process test reader and simulators).</summary>
    public static byte[] RoAccessReport(uint id, IEnumerable<LlrpTag> tags)
    {
        var parts = tags.Select(t =>
        {
            var fields = new List<byte[]>();
            var epc = Convert.FromHexString(t.Epc);
            if (epc.Length == 12) fields.Add(new[] { (byte)(0x80 | LlrpParam.TvEpc96) }.Concat(epc).ToArray());
            else fields.Add(Tlv(LlrpParam.EpcData, U16((ushort)(epc.Length * 8)), epc));
            if (t.AntennaId is int a) fields.Add(new[] { (byte)(0x80 | LlrpParam.TvAntennaId) }.Concat(U16((ushort)a)).ToArray());
            if (t.PeakRssi is double r) fields.Add(new[] { (byte)(0x80 | LlrpParam.TvPeakRssi), unchecked((byte)(sbyte)Math.Round(r)) });
            if (t.SeenCount is int c) fields.Add(new[] { (byte)(0x80 | LlrpParam.TvTagSeenCount) }.Concat(U16((ushort)c)).ToArray());
            if (t.FirstSeenUtc is DateTime f) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, (ulong)((f - DateTime.UnixEpoch).Ticks / 10)); fields.Add(new[] { (byte)(0x80 | LlrpParam.TvFirstSeenUtc) }.Concat(b).ToArray()); }
            return Tlv(LlrpParam.TagReportData, fields.ToArray());
        }).ToArray();
        return EncodeMessage(LlrpMsg.RoAccessReport, id, parts);
    }

    public static byte[] ReaderEventNotification(uint id, bool connectionSuccess = true)
        => EncodeMessage(LlrpMsg.ReaderEventNotification, id, Tlv(LlrpParam.ReaderEventNotificationData, Tlv(LlrpParam.ConnectionAttemptEvent, U16((ushort)(connectionSuccess ? 0 : 1)))));
}
