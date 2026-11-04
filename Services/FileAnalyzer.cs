using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ANUXES.Aftertrace.Models;

namespace ANUXES.Aftertrace.Services;

/// <summary>
/// Performs lightweight, static file inspection. It does not execute files
/// and is not a malware-detection engine.
/// </summary>
public sealed class FileAnalyzer
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".scr", ".sys", ".ocx", ".cpl" };

    private static readonly HashSet<string> DocumentLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png" };

    private static readonly Dictionary<string, string> ExpectedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "Windows PE executable", [".dll"] = "Windows PE executable",
        [".scr"] = "Windows PE executable", [".sys"] = "Windows PE executable",
        [".ocx"] = "Windows PE executable", [".cpl"] = "Windows PE executable",
        [".pdf"] = "PDF document", [".png"] = "PNG image", [".jpg"] = "JPEG image",
        [".jpeg"] = "JPEG image", [".gif"] = "GIF image", [".zip"] = "ZIP archive",
        [".docx"] = "ZIP archive", [".xlsx"] = "ZIP archive", [".pptx"] = "ZIP archive",
        [".jar"] = "ZIP archive", [".apk"] = "ZIP archive", [".7z"] = "7-Zip archive",
        [".rar"] = "RAR archive"
    };

    public Task<FileAnalysisResult> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Analyze(path, cancellationToken), cancellationToken);
    }

    private static FileAnalysisResult Analyze(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("The selected file no longer exists.", path);

        string extension = info.Extension.ToLowerInvariant();
        string detectedType = DetectType(path);
        string hash;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.SequentialScan))
        using (var sha = SHA256.Create())
            hash = Convert.ToHexString(sha.ComputeHash(stream));

        cancellationToken.ThrowIfCancellationRequested();
        string signature = GetSignatureStatus(path);
        var findings = new List<Finding>();

        if (ExpectedTypes.TryGetValue(extension, out string? expected) &&
            !string.Equals(expected, detectedType, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding
            {
                Title = "Extension and file signature differ",
                Description = $"The extension suggests {expected}, but the file header looks like {detectedType}. This can be legitimate, but verify where the file came from.",
                Severity = "Review"
            });
        }

        string nameWithoutFinalExtension = Path.GetFileNameWithoutExtension(info.Name);
        string innerExtension = Path.GetExtension(nameWithoutFinalExtension);
        if (ExecutableExtensions.Contains(extension) && DocumentLikeExtensions.Contains(innerExtension))
        {
            findings.Add(new Finding
            {
                Title = "Executable with a document-like double extension",
                Description = "The filename contains a document or image extension before its executable extension. This naming pattern can be used to disguise executable files.",
                Severity = "Review"
            });
        }

        if (ExecutableExtensions.Contains(extension) && signature.StartsWith("No embedded", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding
            {
                Title = "No embedded signing certificate detected",
                Description = "The executable appears not to contain an embedded Authenticode certificate. Many legitimate tools are unsigned, so this is not proof of malware.",
                Severity = "Notice"
            });
        }

        if (detectedType == "Unknown / unrecognized")
        {
            findings.Add(new Finding
            {
                Title = "Unrecognized file format",
                Description = "AFTERTRACE could not identify a supported file signature. This can happen with proprietary formats or data files.",
                Severity = "Notice"
            });
        }

        if (info.Length == 0)
        {
            findings.Add(new Finding
            {
                Title = "Empty file",
                Description = "The file contains no data. Check whether the download or installation completed successfully.",
                Severity = "Notice"
            });
        }

        if (findings.Count == 0)
        {
            findings.Add(new Finding
            {
                Title = "No obvious warning from basic checks",
                Description = "The checks performed by this version did not find the listed warning patterns. This does not establish that the file is safe.",
                Severity = "Info"
            });
        }

        string assessment = findings.Any(f => f.Severity == "Review")
            ? "Needs review"
            : findings.Any(f => f.Severity == "Notice") ? "No major indicators" : "No obvious warning";

        return new FileAnalysisResult
        {
            AnalyzedAtUtc = DateTime.UtcNow,
            FileName = info.Name,
            FullPath = info.FullName,
            Extension = string.IsNullOrWhiteSpace(extension) ? "(none)" : extension,
            DetectedType = detectedType,
            SizeBytes = info.Length,
            Sha256 = hash,
            SignatureStatus = signature,
            Assessment = assessment,
            Findings = findings
        };
    }

    private static string DetectType(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[16];
        int read = stream.Read(header);
        if (read >= 4 && header[0] == 0x4D && header[1] == 0x5A)
        {
            stream.Position = 0x3C;
            Span<byte> offsetBytes = stackalloc byte[4];
            if (stream.Read(offsetBytes) == 4)
            {
                int peOffset = BitConverter.ToInt32(offsetBytes);
                if (peOffset >= 0 && peOffset <= stream.Length - 4)
                {
                    stream.Position = peOffset;
                    Span<byte> signature = stackalloc byte[4];
                    if (stream.Read(signature) == 4 && signature.SequenceEqual(new byte[] { 0x50, 0x45, 0x00, 0x00 }))
                        return "Windows PE executable";
                }
            }
            return "MZ executable-like file";
        }
        if (read >= 5 && header[..5].SequenceEqual("%PDF-"u8)) return "PDF document";
        if (read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "PNG image";
        if (read >= 3 && header[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) return "JPEG image";
        if (read >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return "GIF image";
        if (read >= 4 && (header[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }) || header[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x05, 0x06 }) || header[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x07, 0x08 }))) return "ZIP archive";
        if (read >= 6 && header[..6].SequenceEqual(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C })) return "7-Zip archive";
        if (read >= 7 && header[..7].SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 })) return "RAR archive";
        if (read >= 4 && header[..4].SequenceEqual(new byte[] { 0x7F, 0x45, 0x4C, 0x46 })) return "ELF executable";
        return "Unknown / unrecognized";
    }

    private static string GetSignatureStatus(string path)
    {
        try
        {
#pragma warning disable SYSLIB0028
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0028
            return $"Embedded certificate found: {certificate.Subject} (trust not verified)";
        }
        catch
        {
            return "No embedded signing certificate detected";
        }
    }
}
