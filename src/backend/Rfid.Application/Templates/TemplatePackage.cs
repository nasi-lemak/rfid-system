using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rfid.Domain.Entities;

namespace Rfid.Application.Templates;

/// <summary>
/// The exchange format for solution templates: definition plus identity, version and an optional publisher
/// signature (RSA-SHA256 over the canonical JSON). Tenants export packages, publishers sign them, and an import
/// verifies the signature against the trusted publishers configured for the deployment.
/// </summary>
public class TemplatePackage
{
    public int FormatVersion { get; set; } = 1;
    public string Code { get; set; } = "custom";
    public string Name { get; set; } = "";
    public string Vertical { get; set; } = "Custom";
    public string Description { get; set; } = "";
    /// <summary>Semantic version of the package content, chosen by the publisher.</summary>
    public string Version { get; set; } = "1.0.0";
    public string? Author { get; set; }
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public TemplateDefinition Definition { get; set; } = new();
    public PackageSignature? Signature { get; set; }
}

public class PackageSignature
{
    public string KeyId { get; set; } = "";
    public string Algorithm { get; set; } = "RSA-SHA256";
    /// <summary>Base64 signature over <see cref="TemplatePackageService.Canonical"/>.</summary>
    public string Value { get; set; } = "";
}

public class TemplateSigningOptions
{
    /// <summary>Refuse imports that are unsigned or not signed by a trusted publisher.</summary>
    public bool RequireSignature { get; set; }
    public SigningKey? Signing { get; set; }
    public List<TrustedPublisher> TrustedPublishers { get; set; } = new();
    public class SigningKey { public string KeyId { get; set; } = ""; public string PrivateKeyPem { get; set; } = ""; }
    public class TrustedPublisher { public string KeyId { get; set; } = ""; public string? Name { get; set; } public string PublicKeyPem { get; set; } = ""; }
}

public enum SignatureStatus { Unsigned, Valid, Invalid, Untrusted }
public record SignatureCheck(SignatureStatus Status, string? KeyId, string? Publisher, string? Reason);

public class TemplatePackageService
{
    private readonly TemplateSigningOptions _o;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public TemplatePackageService(TemplateSigningOptions? options = null) => _o = options ?? new TemplateSigningOptions();

    public bool CanSign => _o.Signing != null && !string.IsNullOrWhiteSpace(_o.Signing.PrivateKeyPem);
    public bool RequireSignature => _o.RequireSignature;

    /// <summary>What the signature covers: identity, version and the definition, serialised deterministically (declaration order, camelCase, no whitespace).</summary>
    public static byte[] Canonical(TemplatePackage p)
    {
        var payload = new { formatVersion = p.FormatVersion, code = p.Code, name = p.Name, vertical = p.Vertical, description = p.Description, version = p.Version, author = p.Author, definition = p.Definition };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json));
    }

    public TemplatePackage Sign(TemplatePackage p)
    {
        if (!CanSign) throw new InvalidOperationException("No signing key configured (Templates:Signing)");
        using var rsa = RSA.Create(); rsa.ImportFromPem(_o.Signing!.PrivateKeyPem);
        p.Signature = new PackageSignature { KeyId = _o.Signing.KeyId, Algorithm = "RSA-SHA256", Value = Convert.ToBase64String(rsa.SignData(Canonical(p), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
        return p;
    }

    public static TemplatePackage Sign(TemplatePackage p, RSA key, string keyId)
    {
        p.Signature = new PackageSignature { KeyId = keyId, Value = Convert.ToBase64String(key.SignData(Canonical(p), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
        return p;
    }

    public SignatureCheck Verify(TemplatePackage p)
    {
        if (p.Signature == null || string.IsNullOrWhiteSpace(p.Signature.Value)) return new(SignatureStatus.Unsigned, null, null, "package carries no signature");
        var pub = _o.TrustedPublishers.FirstOrDefault(t => string.Equals(t.KeyId, p.Signature.KeyId, StringComparison.OrdinalIgnoreCase));
        if (pub == null) return new(SignatureStatus.Untrusted, p.Signature.KeyId, null, $"key '{p.Signature.KeyId}' is not a trusted publisher");
        try
        {
            using var rsa = RSA.Create(); rsa.ImportFromPem(pub.PublicKeyPem);
            var ok = rsa.VerifyData(Canonical(p), Convert.FromBase64String(p.Signature.Value), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return ok ? new(SignatureStatus.Valid, pub.KeyId, pub.Name ?? pub.KeyId, null) : new(SignatureStatus.Invalid, pub.KeyId, pub.Name, "signature does not match the package content");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return new(SignatureStatus.Invalid, p.Signature.KeyId, pub.Name, ex.Message); }
    }

    /// <summary>Import policy: valid signatures always pass; unsigned/untrusted pass only when signatures are not required; invalid never passes.</summary>
    public bool Accept(SignatureCheck check) => check.Status == SignatureStatus.Valid || (!_o.RequireSignature && check.Status is SignatureStatus.Unsigned or SignatureStatus.Untrusted);
}
