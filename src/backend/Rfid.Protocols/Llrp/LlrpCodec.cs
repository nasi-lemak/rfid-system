using System.Buffers.Binary;

namespace Rfid.Protocols.Llrp;

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

// ── Reader configuration, capabilities and GPIO (v1.3) ─────────────────────────────────────────────

public static class LlrpParamEx
{
    public const ushort GeneralDeviceCapabilities = 137, GpioCapabilities = 141, RegulatoryCapabilities = 143, UhfBandCapabilities = 144, TransmitPowerLevelTableEntry = 145,
        GpiTriggerValue = 147, GpoWriteData = 219, AntennaConfiguration = 222, RfReceiver = 223, RfTransmitter = 224, GpiPortCurrentState = 225, GpiEvent = 248,
        C1G2InventoryCommand = 330, C1G2SingulationControl = 336;
    public const ushort MsgGetReaderCapabilitiesResponse = 11;
}

public record ReaderCapabilities(int MaxAntennas, bool HasUtcClock, uint ManufacturerId, uint ModelId, string Firmware, int Gpis, int Gpos, IReadOnlyList<(int index, double dbm)> PowerTable)
{
    public string Manufacturer => ManufacturerId switch { 25882 => "Impinj", 10642 => "Zebra/Motorola", 17740 => "Alien", 4 => "Sirit", _ => $"vendor {ManufacturerId}" };
    public int? PowerIndexFor(double dbm) => PowerTable.Count == 0 ? null : PowerTable.OrderBy(p => Math.Abs(p.dbm - dbm)).First().index;
}

/// <summary>Per-reader inventory settings resolved from device configuration.</summary>
public class LlrpReaderOptions
{
    public double? TransmitPowerDbm { get; set; }
    public int Session { get; set; } = 1;              // Gen2 session 0-3
    public int TagPopulation { get; set; } = 32;
    public ushort[]? AntennaIds { get; set; }          // null/empty = all
    public int? GpiStartPort { get; set; }             // start inventory on this GPI going high, stop on low
    public int ReportEveryNTags { get; set; } = 1;
    public int KeepaliveMs { get; set; } = 10000;
}

public static class LlrpConfigCodec
{
    public static byte[] GetReaderCapabilities(uint id) => LlrpCodec.EncodeMessage(LlrpMsg.GetReaderCapabilities, id, LlrpCodec.U8(0));

    /// <summary>SET_READER_CONFIG carrying per-antenna transmit power and Gen2 session/population.</summary>
    public static byte[] SetAntennaConfig(uint id, LlrpReaderOptions o, ReaderCapabilities? caps)
    {
        var antennas = o.AntennaIds is { Length: > 0 } ? o.AntennaIds : new ushort[] { 0 }; // 0 = all antennas
        var parts = new List<byte[]> { LlrpCodec.U8(0) };
        foreach (var ant in antennas)
        {
            var sub = new List<byte[]> { LlrpCodec.U16(ant) };
            if (o.TransmitPowerDbm is double dbm && caps?.PowerIndexFor(dbm) is int idx)
                sub.Add(LlrpCodec.Tlv(LlrpParamEx.RfTransmitter, LlrpCodec.U16(1), LlrpCodec.U16(0), LlrpCodec.U16((ushort)idx)));
            sub.Add(LlrpCodec.Tlv(LlrpParamEx.C1G2InventoryCommand, LlrpCodec.U8(0),
                LlrpCodec.Tlv(LlrpParamEx.C1G2SingulationControl, LlrpCodec.U8((byte)((Math.Clamp(o.Session, 0, 3) & 3) << 6)), LlrpCodec.U16((ushort)Math.Clamp(o.TagPopulation, 1, 65535)), LlrpCodec.U32(0))));
            parts.Add(LlrpCodec.Tlv(LlrpParamEx.AntennaConfiguration, sub.ToArray()));
        }
        return LlrpCodec.EncodeMessage(LlrpMsg.SetReaderConfig, id, parts.ToArray());
    }

    /// <summary>SET_READER_CONFIG writing a GPO (lamp, buzzer, conveyor stop…).</summary>
    public static byte[] SetGpo(uint id, int port, bool state)
        => LlrpCodec.EncodeMessage(LlrpMsg.SetReaderConfig, id, LlrpCodec.U8(0), LlrpCodec.Tlv(LlrpParamEx.GpoWriteData, LlrpCodec.U16((ushort)port), LlrpCodec.U8((byte)(state ? 0x80 : 0))));

    /// <summary>ADD_ROSPEC variant: start on GPI high (stop when the GPI goes low), for photo-eye / conveyor triggered portals.</summary>
    public static byte[] AddRoSpecGpiTriggered(uint id, uint roSpecId, int gpiPort, ushort reportEveryNTags = 1, ushort[]? antennaIds = null)
    {
        var ants = antennaIds is { Length: > 0 } ? antennaIds : new ushort[] { 0 };
        var antBytes = new List<byte[]> { LlrpCodec.U16((ushort)ants.Length) }; antBytes.AddRange(ants.Select(LlrpCodec.U16));
        var aiSpec = LlrpCodec.Tlv(LlrpParam.AiSpec, antBytes.Concat(new[]
        {
            LlrpCodec.Tlv(LlrpParam.AiSpecStopTrigger, LlrpCodec.U8(2), LlrpCodec.U32(0), LlrpCodec.Tlv(LlrpParamEx.GpiTriggerValue, LlrpCodec.U16((ushort)gpiPort), LlrpCodec.U8(0), LlrpCodec.U32(0))), // stop on GPI low
            LlrpCodec.Tlv(LlrpParam.InventoryParameterSpec, LlrpCodec.U16(1), LlrpCodec.U8(1)),
        }).ToArray());
        var boundary = LlrpCodec.Tlv(LlrpParam.RoBoundarySpec,
            LlrpCodec.Tlv(LlrpParam.RoSpecStartTrigger, LlrpCodec.U8(2), LlrpCodec.Tlv(LlrpParamEx.GpiTriggerValue, LlrpCodec.U16((ushort)gpiPort), LlrpCodec.U8(0x80), LlrpCodec.U32(0))), // start on GPI high
            LlrpCodec.Tlv(LlrpParam.RoSpecStopTrigger, LlrpCodec.U8(0), LlrpCodec.U32(0)));
        const ushort selector = (1 << 15) | (1 << 12) | (1 << 11) | (1 << 10) | (1 << 9) | (1 << 8) | (1 << 7);
        var report = LlrpCodec.Tlv(LlrpParam.RoReportSpec, LlrpCodec.U8(1), LlrpCodec.U16(reportEveryNTags), LlrpCodec.Tlv(LlrpParam.TagReportContentSelector, LlrpCodec.U16(selector)));
        var roSpec = LlrpCodec.Tlv(LlrpParam.RoSpec, LlrpCodec.U32(roSpecId), LlrpCodec.U8(0), LlrpCodec.U8(0), boundary, aiSpec, report);
        return LlrpCodec.EncodeMessage(LlrpMsg.AddRoSpec, id, roSpec);
    }

    public static ReaderCapabilities ParseCapabilities(ReadOnlySpan<byte> body)
    {
        int maxAnt = 0, gpis = 0, gpos = 0; bool utc = false; uint manu = 0, model = 0; var fw = ""; var table = new List<(int, double)>();
        foreach (var p in LlrpCodec.ParseTlvs(body))
        {
            if (p.Type == LlrpParamEx.GeneralDeviceCapabilities && p.Value.Length >= 14)
            {
                maxAnt = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(p.Value);
                utc = (p.Value[2] & 0x40) != 0;
                manu = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(p.Value.AsSpan(4));
                model = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(p.Value.AsSpan(8));
                var len = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(p.Value.AsSpan(12));
                fw = p.Value.Length >= 14 + len ? System.Text.Encoding.UTF8.GetString(p.Value, 14, len) : "";
                foreach (var q in LlrpCodec.ParseTlvs(p.Value.AsSpan(14 + len)))
                    if (q.Type == LlrpParamEx.GpioCapabilities && q.Value.Length >= 4) { gpis = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(q.Value); gpos = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(q.Value.AsSpan(2)); }
            }
            else if (p.Type == LlrpParamEx.RegulatoryCapabilities && p.Value.Length >= 4)
            {
                foreach (var q in LlrpCodec.ParseTlvs(p.Value.AsSpan(4)).Where(q => q.Type == LlrpParamEx.UhfBandCapabilities))
                    foreach (var t in LlrpCodec.ParseTlvs(q.Value).Where(t => t.Type == LlrpParamEx.TransmitPowerLevelTableEntry && t.Value.Length >= 4))
                        table.Add((System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(t.Value), System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(t.Value.AsSpan(2)) / 100.0));
            }
        }
        return new ReaderCapabilities(maxAnt, utc, manu, model, fw, gpis, gpos, table);
    }

    /// <summary>Builds a GET_READER_CAPABILITIES_RESPONSE (fake readers / tests).</summary>
    public static byte[] CapabilitiesResponse(uint id, int antennas, uint manufacturer, uint model, string firmware, int gpis, int gpos, IEnumerable<(int index, double dbm)> powerTable)
    {
        var fwb = System.Text.Encoding.UTF8.GetBytes(firmware);
        var general = LlrpCodec.Tlv(LlrpParamEx.GeneralDeviceCapabilities, LlrpCodec.U16((ushort)antennas), LlrpCodec.U16(0x4000), LlrpCodec.U32(manufacturer), LlrpCodec.U32(model), LlrpCodec.U16((ushort)fwb.Length), fwb,
            LlrpCodec.Tlv(LlrpParamEx.GpioCapabilities, LlrpCodec.U16((ushort)gpis), LlrpCodec.U16((ushort)gpos)));
        var entries = powerTable.Select(p => LlrpCodec.Tlv(LlrpParamEx.TransmitPowerLevelTableEntry, LlrpCodec.U16((ushort)p.index), LlrpCodec.U16(unchecked((ushort)(short)Math.Round(p.dbm * 100))))).ToArray();
        var regulatory = LlrpCodec.Tlv(LlrpParamEx.RegulatoryCapabilities, LlrpCodec.U16(826), LlrpCodec.U16(1), LlrpCodec.Tlv(LlrpParamEx.UhfBandCapabilities, entries));
        return LlrpCodec.EncodeMessage(LlrpParamEx.MsgGetReaderCapabilitiesResponse, id, LlrpCodec.Status(0), general, regulatory);
    }

    /// <summary>Extracts GPI events from a READER_EVENT_NOTIFICATION body.</summary>
    public static List<(int port, bool high)> ParseGpiEvents(ReadOnlySpan<byte> body)
    {
        var list = new List<(int, bool)>();
        foreach (var p in LlrpCodec.ParseTlvs(body).Where(p => p.Type == LlrpParam.ReaderEventNotificationData))
            foreach (var q in LlrpCodec.ParseTlvs(p.Value).Where(q => q.Type == LlrpParamEx.GpiEvent && q.Value.Length >= 3))
                list.Add((System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(q.Value), (q.Value[2] & 0x80) != 0));
        return list;
    }

    public static byte[] GpiEventNotification(uint id, int port, bool high)
        => LlrpCodec.EncodeMessage(LlrpMsg.ReaderEventNotification, id, LlrpCodec.Tlv(LlrpParam.ReaderEventNotificationData, LlrpCodec.Tlv(LlrpParamEx.GpiEvent, LlrpCodec.U16((ushort)port), LlrpCodec.U8((byte)(high ? 0x80 : 0)))));
}
