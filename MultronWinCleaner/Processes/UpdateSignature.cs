using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MultronWinCleaner.Processes
{
    public static class UpdateSignature
    {
        public const string ManifestName = "update.json";
        public const string SignatureName = "update.json.sig";
        public const string AppManifest = "multron-win-cleaner";
        public const string DatabaseManifest = "multron-win-cleaner-database";
        private static readonly string[] TrustedHosts = { "github.com", "api.github.com" };

        public static bool IsTrustedUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && TrustedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

        public static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        private static InvalidDataException NotVerified() =>
            new InvalidDataException(Loc.T("The update signature could not be verified, so the update was not installed."));

        public static IReadOnlyDictionary<string, string> VerifyManifest(byte[] manifest, string signature, string publicKey, Version version, Version current) =>
            VerifyManifest(manifest, signature, publicKey, AppManifest, version, current, false);

        public static IReadOnlyDictionary<string, string> VerifyManifest(byte[] manifest, string signature, string publicKey, string app, Version version, Version current, bool allowSameVersion)
        {
            if (string.IsNullOrWhiteSpace(publicKey))
                throw NotVerified();
            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature.Trim());
            }
            catch (FormatException)
            {
                throw NotVerified();
            }

            using (ECDsa key = ECDsa.Create())
            {
                try
                {
                    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey.Trim()), out _);
                }
                catch (Exception ex) when (ex is FormatException || ex is CryptographicException)
                {
                    throw NotVerified();
                }
                if (!key.VerifyData(manifest, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    throw NotVerified();
            }

            JObject json;
            try
            {
                json = JObject.Parse(Encoding.UTF8.GetString(manifest));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                throw NotVerified();
            }
            if ((string?)json["name"] != app || json["files"] is not JObject files)
                throw NotVerified();
            if (!Version.TryParse((string?)json["version"], out Version? signedVersion))
                throw NotVerified();

            Version signed = Normalize(signedVersion);
            if (signed != Normalize(version))
                throw new InvalidDataException(Loc.F("The signed update is for version {0}, not {1}, so it was not installed.", signed, Normalize(version)));
            bool older = allowSameVersion ? signed < Normalize(current) : signed <= Normalize(current);
            if (older)
                throw new InvalidDataException(Loc.F("Version {0} is not newer than the installed version {1}, so it was not installed.", signed, Normalize(current)));

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in files.Properties())
            {
                string? hash = (string?)property.Value;
                if (hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit))
                    hashes[property.Name] = hash.ToLowerInvariant();
            }
            return hashes;
        }

        public static string ExpectedHash(IReadOnlyDictionary<string, string> hashes, string fileName) =>
            hashes.TryGetValue(fileName, out string? hash) ? hash : throw NotVerified();

        public static string Sha256File(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public static void VerifyFile(string path, string expectedHash)
        {
            if (!string.Equals(Sha256File(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Loc.T("The update package does not match its signed checksum and was not installed."));
        }
    }
}
