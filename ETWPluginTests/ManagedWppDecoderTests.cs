using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using findneedle.Wpp;

namespace ETWPluginTests;

/// <summary>End-to-end: decode a real WppEmitter .etl in fully managed code (TraceEvent to read the wire +
/// our TMF/format engine), with NO tracefmt.exe, NO WDK, NO admin. The fixture is a 16 KB capture committed
/// under WppFixtures/; expected strings are exactly what tracefmt emits for WppEmitter's two statements.</summary>
[TestClass]
public sealed class ManagedWppEndToEndTests
{
    private static string FixtureEtl()
        => Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wppemitter-sample.etl");

    [TestMethod]
    public void ManagedDecode_RealWppEmitterEtl_MatchesTracefmt()
    {
        var etl = FixtureEtl();
        if (!File.Exists(etl)) Assert.Inconclusive($"fixture missing: {etl}");

        var tmf = TmfDatabase.LoadDirectory(MixedFilterFixtureGenerator.WppEmitterTmfDir());
        Assert.AreEqual(2, tmf.Count, "the WppEmitter TMF should load (2 statements)");

        var decoder = new ManagedWppEtlDecoder(tmf);
        var events = decoder.DecodeToList(etl);

        // The capture is `WppEmitter.exe 30`: 30 TRACE_GENERAL "work item" (msg 10) + one "detail" (msg 11)
        // per even i (i=0,2,…,28 → 15). All fully managed-decoded.
        int workItems = events.Count(e => e.MessageNumber == 10);
        int details = events.Count(e => e.MessageNumber == 11);
        Assert.AreEqual(30, workItems, "30 work-item events");
        Assert.AreEqual(15, details, "15 detail events (one per even i)");

        // Exact-string parity with tracefmt for representative events (incl. hex-formatted status).
        var messages = new HashSet<string>(events.Select(e => e.Message));
        Assert.IsTrue(messages.Contains("WppEmitter work item id=0 status=0x0 phase=startup provider=WppEmitter"),
            "i=0 work item");
        Assert.IsTrue(messages.Contains("WppEmitter work item id=27 status=0x1b phase=startup provider=WppEmitter"),
            "i=27 work item — proves %x hex formatting (27 = 0x1b)");
        Assert.IsTrue(messages.Contains("WppEmitter detail seq=2 note=processing-record value=2 category=Detail"),
            "i=2 detail");

        // Component + wire fields came through.
        var any = events.First();
        Assert.AreEqual("findneedle", any.Component);
        Assert.AreNotEqual(0, any.ProcessId);
        Assert.AreNotEqual(default(DateTime), any.TimeStamp);
    }

    [TestMethod]
    public void ManagedDecode_RealStringArgs_MatchesTracefmt()
    {
        // A real WPP capture with STRING args: msg 10 = "%s (ANSI) + %d", msg 11 = "%ws (wide)". The
        // fixture's TMF is committed alongside it. Expected strings verified byte-for-byte against tracefmt.
        var etl = Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wppstr-sample.etl");
        if (!File.Exists(etl)) Assert.Inconclusive($"fixture missing: {etl}");

        // The shared tmf/ dir holds several fixtures' TMFs; lookup is by GUID, so just require ours loaded.
        var tmf = TmfDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "WppFixtures", "tmf"));
        Assert.IsTrue(tmf.TryGet(Guid.Parse("744151fd-b3f4-32e6-38eb-0fd11e3fb62d"), 10, out _),
            "the string-emitter TMF should be loaded");

        var events = new ManagedWppEtlDecoder(tmf).DecodeToList(etl);
        var messages = events.Select(e => e.Message).ToList();

        // ItemString (ANSI, NUL-terminated) + ItemLong, three iterations:
        CollectionAssert.Contains(messages, "strtrace name=alpha id=0 tag=END");
        CollectionAssert.Contains(messages, "strtrace name=bravo id=1 tag=END");
        CollectionAssert.Contains(messages, "strtrace name=charlie id=2 tag=END");
        // ItemWString (UTF-16, NUL-terminated):
        CollectionAssert.Contains(messages, "widetrace user=root role=admin");
    }

    [TestMethod]
    public void ManagedDecode_Win32AndPointerTypes_MatchesTracefmt()
    {
        // A real capture exercising the common C++/Win32 + pointer types. Every expected string was verified
        // byte-for-byte against tracefmt (tools/WppTypesEmitter). Covers: 32/64-bit signed/unsigned/hex ints,
        // %p pointers (uppercase, pointer-width padded), width/zero-pad, HRESULT + NTSTATUS (hex + symbolic),
        // and GUID.
        var etl = Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wpptypes-sample.etl");
        if (!File.Exists(etl)) Assert.Inconclusive($"fixture missing: {etl}");

        var tmf = TmfDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "WppFixtures", "tmf"));
        var messages = new ManagedWppEtlDecoder(tmf).DecodeToList(etl).Select(e => e.Message).ToList();

        CollectionAssert.Contains(messages, "sint i=42 neg=-1000 i64=-5000000000");
        CollectionAssert.Contains(messages, "uint u=4000000000 hex=0xabcdef x64=0xdeadbeefcafe");
        CollectionAssert.Contains(messages, "ptr p=00007FF012345678 null=0000000000000000");
        CollectionAssert.Contains(messages, "widths z=00042 h=0000beef");
        CollectionAssert.Contains(messages,
            "hr=0x80070005(ERROR_ACCESS_DENIED) st=0xc0000022(STATUS_ACCESS_DENIED) guid=11223344-5566-7788-99aa-bbccddeeff00");
    }
}

/// <summary>
/// Prototype: decode WPP in MANAGED code (no WDK/tracefmt.exe). tracefmt is a thin front-end over the OS
/// WPP format engine; this exercises a managed reimplementation of the two halves we can test without a
/// live capture: the TMF parser (<see cref="TmfDatabase"/>) and the arg-decode + printf format engine
/// (<see cref="WppMessageFormatter"/>). Fixtures are WppEmitter's committed .tmf + its known source, so the
/// expected strings are exactly what tracefmt emits for those two trace statements. The event→(guid,msgNum,
/// blob) WIRE read is the remaining (capture-format-dependent) piece and is prototyped/gated separately.
/// </summary>
[TestClass]
public sealed class ManagedWppDecoderTests
{
    private static readonly Guid WppEmitterGuid = Guid.Parse("9b93a332-c452-3e71-64f7-55130c7de2e4");

    private static TmfDatabase LoadWppEmitterTmf()
        => TmfDatabase.LoadDirectory(MixedFilterFixtureGenerator.WppEmitterTmfDir());

    // ---- TMF parser ----

    [TestMethod]
    public void Tmf_Parses_BothWppEmitterStatements()
    {
        var db = LoadWppEmitterTmf();
        Assert.AreEqual(2, db.Count, "WppEmitter.cpp has two DoTraceMessage statements → two TMF entries");

        Assert.IsTrue(db.TryGet(WppEmitterGuid, 10, out var workItem), "message #10 (the TRACE_GENERAL 'work item')");
        Assert.AreEqual("WppEmitter_cpp36", workItem.Tag);
        Assert.AreEqual("findneedle", workItem.Component);
        CollectionAssert.AreEqual(new[] { 10, 11 }, workItem.Args.Select(a => a.ArgNumber).ToArray());
        CollectionAssert.AreEqual(new[] { "ItemLong", "ItemLong" }, workItem.Args.Select(a => a.TypeName).ToArray());

        Assert.IsTrue(db.TryGet(WppEmitterGuid, 11, out var detail), "message #11 (the TRACE_DETAIL 'detail')");
        Assert.AreEqual("WppEmitter_cpp40", detail.Tag);
    }

    // ---- arg-decode + format engine, end-to-end against the real TMF ----

    private static byte[] TwoInts(int a, int b)
        => BitConverter.GetBytes(a).Concat(BitConverter.GetBytes(b)).ToArray();

    [TestMethod]
    public void Format_WorkItem_MatchesTracefmtOutput()
    {
        var db = LoadWppEmitterTmf();
        Assert.IsTrue(db.TryGet(WppEmitterGuid, 10, out var e));

        // WppEmitter.cpp:36 emits (id=i, status=(i & 0xff)). For i=427: id=427, status=0xAB.
        var msg = WppMessageFormatter.Format(e, TwoInts(427, 0xAB));

        Assert.AreEqual("WppEmitter work item id=427 status=0xab phase=startup provider=WppEmitter", msg);
    }

    [TestMethod]
    public void Format_Detail_MatchesTracefmtOutput()
    {
        var db = LoadWppEmitterTmf();
        Assert.IsTrue(db.TryGet(WppEmitterGuid, 11, out var e));

        // WppEmitter.cpp:40 emits (seq=i, value=i % 7919). For i=42: seq=42, value=42.
        var msg = WppMessageFormatter.Format(e, TwoInts(42, 42));

        Assert.AreEqual("WppEmitter detail seq=42 note=processing-record value=42 category=Detail", msg);
    }

    // ---- printf-spec engine (isolated) ----

    [TestMethod]
    public void ApplyFormat_HandlesCommonSpecs()
    {
        var args = new Dictionary<int, object>
        {
            [10] = 255,        // int
            [11] = (uint)4096, // uint
            [12] = "hello",    // string
        };
        // decimal, lowercase hex, uppercase hex, zero-padded hex, unsigned, string, and %% literal.
        Assert.AreEqual("255", WppMessageFormatter.ApplyFormat("%10!d!", args));
        Assert.AreEqual("ff", WppMessageFormatter.ApplyFormat("%10!x!", args));
        Assert.AreEqual("FF", WppMessageFormatter.ApplyFormat("%10!X!", args));
        Assert.AreEqual("00ff", WppMessageFormatter.ApplyFormat("%10!04x!", args));
        Assert.AreEqual("4096", WppMessageFormatter.ApplyFormat("%11!u!", args));
        Assert.AreEqual("hello", WppMessageFormatter.ApplyFormat("%12!s!", args));
        Assert.AreEqual("100%", WppMessageFormatter.ApplyFormat("%10!d!%%", new Dictionary<int, object> { [10] = 100 }));
    }

    [TestMethod]
    public void ApplyFormat_Prefix_And_MissingArgs_RenderEmpty()
    {
        // %0 (WPP prefix) and reserved/missing %1..%9 render as nothing, so the surrounding text still forms.
        var args = new Dictionary<int, object> { [10] = 7 };
        Assert.AreEqual("val=7", WppMessageFormatter.ApplyFormat("%0val=%10!d!", args));
        Assert.AreEqual("a=b=7", WppMessageFormatter.ApplyFormat("a=%3!d!b=%10!d!", args));
    }

    // ---- arg decoder (isolated) ----

    [TestMethod]
    public void DecodeArgs_ReadsTypedValuesInBlobOrder()
    {
        var e = new TmfEntry
        {
            MessageGuid = WppEmitterGuid,
            MessageNumber = 99,
            Format = "%10!d! %11!x!",
            Args = new[] { new TmfArg(10, "ItemLong"), new TmfArg(11, "ItemULong") },
        };
        var vals = WppMessageFormatter.DecodeArgs(e, TwoInts(-5, unchecked((int)0xDEADBEEF)));
        Assert.AreEqual(-5, (int)vals[10]);
        Assert.AreEqual(0xDEADBEEFu, (uint)vals[11]);
    }
}
