/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// What the application tells a helper it starts, on the helper's standard input and
/// nowhere else: a certificate with its private key to serve TLS with, and a token every
/// call must carry. Neither touches the disk, the command line or the environment.
/// </summary>
public sealed record SteamWorkerHandshake(string PfxBase64, string Token)
{
    /// <summary>The header every call carries the token in.</summary>
    public const string TokenHeader = "x-caretaker-token";

    /// <summary>The trailer a failed call carries the <see cref="ProviderException.ErrorCode"/> in.</summary>
    public const string ErrorCodeTrailer = "x-caretaker-error";

    /// <summary>A fresh certificate for 127.0.0.1 and a fresh token, for one launch.</summary>
    public static SteamWorkerHandshake Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=caretaker-steam-worker", key, HashAlgorithmName.SHA256);
        SubjectAlternativeNameBuilder names = new();
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return new SteamWorkerHandshake(
            Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    /// <summary>The certificate with its key, for the helper to serve with.</summary>
    public X509Certificate2 LoadCertificate() =>
        X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(PfxBase64), null, X509KeyStorageFlags.Exportable);

    /// <summary>The certificate's thumbprint, which is all the application needs to recognise the helper.</summary>
    public string Thumbprint
    {
        get
        {
            using var certificate = LoadCertificate();
            return certificate.Thumbprint;
        }
    }

    public string ToLine() => JsonSerializer.Serialize(this);

    public static SteamWorkerHandshake FromLine(string line) =>
        JsonSerializer.Deserialize<SteamWorkerHandshake>(line)
            ?? throw new InvalidOperationException("Empty handshake.");
}
