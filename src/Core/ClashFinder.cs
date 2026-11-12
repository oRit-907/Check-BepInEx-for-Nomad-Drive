using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NomadModDownloader.Core;

public enum ClashKind { Duplicate, TwoVersions, Library, Patches }

/// <summary>A problem between mods: the same one twice, two versions of a library, or two mods changing the same code.</summary>
public sealed record Clash(ClashKind Kind, string Title, string Detail, List<string> Mods, string? FixRel = null);

/// <summary>
/// Finds clashes between the mods that are switched on in BepInEx\plugins:
/// - the same plugin (BepInEx id) in more than one place: BepInEx loads one and skips the rest;
/// - the same mod twice under different ids (two versions side by side);
/// - two different versions of the same support library (only the first one loaded is used);
/// - two mods that patch the same game method with Harmony ([HarmonyPatch] attributes): not always a problem,
///   but the first place to look when one of them misbehaves.
/// Everything is read from the DLLs' metadata; nothing is loaded.
/// </summary>
public static class ClashFinder
{
    public static List<Clash> Find(GameInstall game)
    {
        var result = new List<Clash>();
        var mods = ModEntries.Scan(game).Where(m => m.On).ToList();
        string RelOf(string full) => Path.GetRelativePath(game.Root, full).Replace('\\', '/');
        string OwnerOf(string dll) => mods.FirstOrDefault(m => m.Dlls.Contains(dll))?.Name ?? Path.GetFileNameWithoutExtension(dll);

        // 1. The same plugin id more than once.
        var plugins = mods.SelectMany(m => m.Dlls.SelectMany(PluginInspector.Read)).ToList();
        foreach (var g in plugins.GroupBy(p => p.Guid, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(p => p.File).Distinct().Count() > 1))
        {
            var copies = g.OrderByDescending(p => ParseVersion(p.Version)).ToList();
            var older = copies.Skip(1).Select(p => mods.First(m => m.Dlls.Contains(p.File))).Distinct().ToList();
            result.Add(new Clash(ClashKind.Duplicate,
                L.F("{0} is installed {1} times", copies[0].Name, copies.Count),
                L.T("BepInEx loads only one copy and skips the others, so you may not get the version you expect. Keep the newest and switch the others off:") + "\n"
                + string.Join("\n", copies.Select(p => $"• v{p.Version}  {RelOf(p.File)}")),
                copies.Select(p => OwnerOf(p.File)).Distinct().ToList(),
                older.Count == 1 && older[0].Plugins.All(p => string.Equals(p.Guid, g.Key, StringComparison.OrdinalIgnoreCase)) ? older[0].Rel : null));
        }

        // 2. The same mod name under different ids (usually an old and a new version side by side).
        foreach (var g in plugins.GroupBy(p => Key(p.Name)).Where(g => g.Key.Length > 2 && g.Select(p => p.Guid.ToLowerInvariant()).Distinct().Count() > 1))
            result.Add(new Clash(ClashKind.TwoVersions,
                L.F("Two different mods are both called {0}", g.First().Name),
                L.T("They have different ids, so BepInEx starts both. This is often an old and a new version of the same mod side by side:") + "\n"
                + string.Join("\n", g.Select(p => $"• {p.Guid} v{p.Version}  {RelOf(p.File)}")),
                g.Select(p => OwnerOf(p.File)).Distinct().ToList()));

        // 3. Different versions of one support library (DLLs that aren't plugins).
        var libs = new List<(string Name, Version Version, string File)>();
        foreach (var m in mods)
            foreach (var d in m.Dlls)
                if (AssemblyIdentity(d) is { } id && PluginInspector.Read(d).Count == 0) libs.Add((id.Name, id.Version, d));
        foreach (var g in libs.GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(l => l.Version).Distinct().Count() > 1))
            result.Add(new Clash(ClashKind.Library,
                L.F("Mods bring different versions of {0}", g.Key),
                L.T("Only the first copy the game loads is used, so a mod made for the other version may fail. Ask the mods' authors which one they need:") + "\n"
                + string.Join("\n", g.OrderByDescending(l => l.Version).Select(l => $"• v{l.Version}  {RelOf(l.File)}")),
                g.Select(l => OwnerOf(l.File)).Distinct().ToList()));

        // 4. Harmony patches on the same game method from more than one mod.
        var patches = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var m in mods)
            foreach (var d in m.Dlls)
                foreach (var target in HarmonyTargets(d))
                    (patches.TryGetValue(target, out var set) ? set : patches[target] = new(StringComparer.OrdinalIgnoreCase)).Add(m.Name);
        foreach (var g in patches.Where(p => p.Value.Count > 1).GroupBy(p => string.Join(" + ", p.Value.OrderBy(x => x))))
        {
            var methods = g.Select(x => x.Key).OrderBy(x => x).ToList();
            result.Add(new Clash(ClashKind.Patches,
                L.F("{0} change the same game code", g.Key),
                L.F("Both patch {0} game method(s). That often works fine, but if one of these mods misbehaves, the other is the first suspect:", methods.Count) + "\n"
                + string.Join("\n", methods.Take(12).Select(x => "• " + x)) + (methods.Count > 12 ? "\n" + L.F("…and {0} more", methods.Count - 12) : ""),
                g.First().Value.ToList()));
        }
        return result;
    }

    private static string Key(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Version ParseVersion(string v) => Version.TryParse(new string(v.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.'), out var r) ? r : new Version(0, 0);

    private static (string Name, Version Version)? AssemblyIdentity(string dll)
    {
        try
        {
            using var fs = File.OpenRead(dll);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) return null;
            var a = md.GetAssemblyDefinition();
            return (md.GetString(a.Name), a.Version);
        }
        catch { return null; }
    }

    /// <summary>
    /// "Type.Method" for every [HarmonyPatch] in a DLL. A class-level [HarmonyPatch(typeof(T))] or
    /// [HarmonyPatch(typeof(T), "Method")] is combined with method-level ones, like Harmony does.
    /// </summary>
    public static List<string> HarmonyTargets(string dll)
    {
        var found = new List<string>();
        try
        {
            using var fs = File.OpenRead(dll);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return found;
            var md = pe.GetMetadataReader();
            var provider = new NameProvider(md);
            foreach (var th in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(th);
                var (cType, cMethod) = Read(md, provider, type.GetCustomAttributes());
                var any = false;
                foreach (var mh in type.GetMethods())
                {
                    var (mType, mMethod) = Read(md, provider, md.GetMethodDefinition(mh).GetCustomAttributes());
                    if (mType == null && mMethod == null) continue;
                    var t = mType ?? cType; var m = mMethod ?? cMethod;
                    if (t != null && m != null) { found.Add(Short(t) + "." + m); any = true; }
                }
                if (!any && cType != null && cMethod != null) found.Add(Short(cType) + "." + cMethod);
            }
        }
        catch { }
        return found.Distinct().ToList();
    }

    private static string Short(string type) => type.Contains('.') ? type[(type.LastIndexOf('.') + 1)..] : type;

    private static (string? Type, string? Method) Read(MetadataReader md, NameProvider provider, CustomAttributeHandleCollection attrs)
    {
        string? type = null, method = null;
        foreach (var ah in attrs)
        {
            var attr = md.GetCustomAttribute(ah);
            if (AttributeName(md, attr.Constructor) is not ("HarmonyPatch" or "HarmonyPatchAttribute")) continue;
            CustomAttributeValue<string> v;
            try { v = attr.DecodeValue(provider); } catch { continue; }
            foreach (var a in v.FixedArguments)
            {
                if (a.Type == "System.Type" && a.Value is string t) type ??= t;
                else if (a.Type == "System.String" && a.Value is string s && method == null) method = s;
            }
        }
        return (type, method);
    }

    private static string AttributeName(MetadataReader md, EntityHandle ctor)
    {
        if (ctor.Kind == HandleKind.MemberReference)
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)ctor);
            if (mr.Parent.Kind == HandleKind.TypeReference) return md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name);
        }
        else if (ctor.Kind == HandleKind.MethodDefinition)
            return md.GetString(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType()).Name);
        return "";
    }

    /// <summary>Decodes attribute arguments as type names (enough to read typeof(...) and strings).</summary>
    private sealed class NameProvider : ICustomAttributeTypeProvider<string>
    {
        private readonly MetadataReader _md;
        public NameProvider(MetadataReader md) { _md = md; }
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var t = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(t.Namespace);
            return (ns.Length > 0 ? ns + "." : "") + reader.GetString(t.Name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var t = reader.GetTypeReference(handle);
            var ns = reader.GetString(t.Namespace);
            return (ns.Length > 0 ? ns + "." : "") + reader.GetString(t.Name);
        }
        public string GetTypeFromSerializedName(string name) => name.Split(',')[0];
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32; // MethodType and the like
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
