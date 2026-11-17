using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length < 2)
    throw new ArgumentException("Usage: PackageProbe <PawPrints folder> <sample.png>");

string package = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string local = Path.Combine(package, name.Name + ".dll");
    return File.Exists(local) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(local) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package, "PawPrints.dll"));
if (assembly.GetName().Name != "PawPrints")
    throw new InvalidDataException("Incorrect packaged runtime assembly identity.");
Console.WriteLine("PASS PawPrints runtime assembly identity.");

// Check the shipped font bytes, not just the source files used during a build.
// The release audit separately compares packaged provenance to the source index.
using var provenance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package, "Font-Licenses", "source.json")));
var expectedResources = new HashSet<string>(StringComparer.Ordinal);
foreach (var font in provenance.RootElement.GetProperty("fonts").EnumerateArray())
{
    string slug = font.GetProperty("slug").GetString()!;
    string resource = $"PawPrints.Fonts.{slug}.ttf";
    if (!expectedResources.Add(resource))
        throw new InvalidDataException("Duplicate font provenance: " + slug);
    bool sawFont = false;
    bool sawLicense = false;
    foreach (var record in font.GetProperty("files").EnumerateArray())
    {
        string file = record.GetProperty("file").GetString()!;
        string expectedHash = record.GetProperty("sha256").GetString()!;
        byte[] bytes;
        if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
        {
            if (sawFont)
                throw new InvalidDataException("Duplicate font file: " + slug);
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException("Missing font resource: " + resource);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            sawFont = true;
        }
        else if (file == "OFL.txt")
        {
            if (sawLicense)
                throw new InvalidDataException("Duplicate OFL notice: " + slug);
            bytes = File.ReadAllBytes(Path.Combine(package, "Font-Licenses", slug + "-OFL.txt"));
            sawLicense = true;
        }
        else
            throw new InvalidDataException("Unexpected font provenance file: " + file);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Packaged font/license hash mismatch: " + slug + "/" + file);
    }
    if (!sawFont || !sawLicense)
        throw new InvalidDataException("Incomplete font provenance: " + slug);
    Console.WriteLine("PASS embedded font and packaged OFL bytes: " + slug);
}
var actualResources = assembly.GetManifestResourceNames()
    .Where(name => name.StartsWith("PawPrints.Fonts.", StringComparison.Ordinal));
if (expectedResources.Count != 6 || !expectedResources.SetEquals(actualResources))
    throw new InvalidDataException("Embedded font inventory does not match the six licensed fonts");

// The editable release rules and their embedded recovery copy must agree.
using (var stream = assembly.GetManifestResourceStream("PawPrints.PartSupport.json")
    ?? throw new InvalidDataException("Missing embedded part-support definitions"))
{
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    if (!buffer.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(package, "part-support.json"))))
        throw new InvalidDataException("Packaged support definitions differ from the embedded release copy");
}
var supportType = assembly.GetType("Blueprint.Rendering.PartSupport", true)!;
var support = supportType.GetMethod("Load")!.Invoke(null, [package])!;
foreach (var (id, expected) in new[] { ("CoreSolarPanelA_CellA", "solar"), ("CoreFoilA", "foil"), ("OtherMod_Part", "structure") })
    if ((string)supportType.GetMethod("Surface")!.Invoke(support, [id])! != expected)
        throw new InvalidDataException("Packaged support definition mismatch: " + id);
Console.WriteLine("PASS packaged and embedded support definitions match and load correctly.");

var logo = assembly.GetType("Blueprint.Rendering.CompanyLogo", true)!;
foreach (string file in args.Skip(1))
{
    var result = logo.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, [Path.GetFullPath(file)])!;
    var resultType = result.GetType();
    Console.WriteLine($"PASS packaged {Path.GetExtension(file)}: {resultType.GetProperty("Width")!.GetValue(result)} x {resultType.GetProperty("Height")!.GetValue(result)}");
}
if (assembly.GetReferencedAssemblies().Any(a => a.Name == "SkiaSharp") ||
    Directory.EnumerateFiles(package, "*Skia*", SearchOption.AllDirectories).Any())
    throw new Exception("Unexpected image-library dependency in package");
Console.WriteLine("PASS Windows PNG decoding without bundled image-codec libraries.");

var fonts = assembly.GetType("Blueprint.Rendering.InitialsFont", true)!;
var names = (string[])fonts.GetField("Names", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
foreach (var name in names)
{
    fonts.GetMethod("EnsureLoaded", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [name]);
    Console.WriteLine("PASS packaged private font: " + name);
}
