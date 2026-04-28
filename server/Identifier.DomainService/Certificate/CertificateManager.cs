using Blocks.Genesis;
using DomainService.Shared;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DomainService.Certificate
{
    public class CertificateManager : ICertificateManager
    {
        private readonly ICertificateStorageFactory _certificateStorageFactory;
        private readonly ICryptoService _cryptoService;

        public CertificateManager(ICertificateStorageFactory certificateStorageFactory, ICryptoService cryptoService)
        {
            _certificateStorageFactory = certificateStorageFactory;
            _cryptoService = cryptoService;
        }

        private SecureRandom CreateSecureRandom()
        {
            var cryptoApiRandomGenerator = new CryptoApiRandomGenerator();
            var secureRandom = new SecureRandom(cryptoApiRandomGenerator);

            return secureRandom;
        }

        private AsymmetricCipherKeyPair CreateAsymmetricCipherKeyPair(SecureRandom secureRandom)
        {
            var keyGenerationParameters = new KeyGenerationParameters(secureRandom, IdentifierConstants.KeyLength);
            var rsaKeyPairGenerator = new RsaKeyPairGenerator();
            rsaKeyPairGenerator.Init(keyGenerationParameters);
            var asymmetricCipherKeyPair = rsaKeyPairGenerator.GenerateKeyPair();

            return asymmetricCipherKeyPair;
        }

        private X509V3CertificateGenerator CreateCertificateGenerator(JwtTokenParameters parameters,
                                                                     AsymmetricCipherKeyPair keyPair,
                                                                     SecureRandom secureRandom,
                                                                     DateTime notBefore,
                                                                     DateTime notAfter,
                                                                     out string serialNumberString)
        {

            X509V3CertificateGenerator certificateGenerator = new X509V3CertificateGenerator();
            BigInteger serialNumber = BigInteger.ProbablePrime(128, secureRandom);

            certificateGenerator.SetSerialNumber(serialNumber);
            certificateGenerator.SetIssuerDN(new X509Name($"CN={parameters.Issuer}"));
            certificateGenerator.SetSubjectDN(new X509Name($"CN={parameters.Subject}"));
            certificateGenerator.SetNotAfter(notAfter);
            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetPublicKey(keyPair.Public);

            serialNumberString = serialNumber.ToString();

            return certificateGenerator;
        }

        public (X509Certificate2, X509Certificate2) GenerateCertificates(JwtTokenParameters parameters)
        {
            var serialNumber = string.Empty;
            var secureRandom = CreateSecureRandom();
            var asymmetricCipherKeyPair = CreateAsymmetricCipherKeyPair(secureRandom);
            var notBefore = DateTime.UtcNow.Date;
            var notAfter = notBefore.AddDays(parameters.CertificateValidForNumberOfDays);

            var publicKeyCertificate = CreatePublicKeyCertificate(parameters, asymmetricCipherKeyPair, secureRandom, notBefore, notAfter, out serialNumber);
            var privateKeyCertificate = CreatePrivateKeyCertificate(parameters, publicKeyCertificate, asymmetricCipherKeyPair, secureRandom);

            return (publicKeyCertificate, privateKeyCertificate);
        }

        private X509Certificate2 CreatePrivateKeyCertificate(JwtTokenParameters parameters, System.Security.Cryptography.X509Certificates.X509Certificate publicKeyCertificate, AsymmetricCipherKeyPair asymmetricCipherKeyPair, SecureRandom secureRandom)
        {
            if (publicKeyCertificate == null) throw new ArgumentNullException(nameof(publicKeyCertificate));
            if (asymmetricCipherKeyPair == null) throw new ArgumentNullException(nameof(asymmetricCipherKeyPair));
            if (secureRandom == null) throw new ArgumentNullException(nameof(secureRandom));

            var bcCertificate = DotNetUtilities.FromX509Certificate(publicKeyCertificate);

            var store = new Pkcs12StoreBuilder().Build();
            var certEntry = new X509CertificateEntry(bcCertificate);
            var alias = publicKeyCertificate.Subject;

            store.SetCertificateEntry(alias, certEntry);
            store.SetKeyEntry(alias, new AsymmetricKeyEntry(asymmetricCipherKeyPair.Private), new[] { certEntry });

            using (var stream = new MemoryStream())
            {
                store.Save(stream, parameters.PrivateCertificatePassword.ToCharArray(), secureRandom);
                return new X509Certificate2(stream.ToArray(), parameters.PrivateCertificatePassword, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            }
        }

        private X509Certificate2 CreatePublicKeyCertificate(JwtTokenParameters parameters, AsymmetricCipherKeyPair asymmetricCipherKeyPair, SecureRandom secureRandom, DateTime notBefore, DateTime notAfter, out string serialNumber)
        {
            var publicKeyCertificateGenerator = CreateCertificateGenerator(parameters, asymmetricCipherKeyPair, secureRandom, notBefore, notAfter, out serialNumber);
            var signatureFactory = new Asn1SignatureFactory(IdentifierConstants.AlgorithmName, asymmetricCipherKeyPair.Private, secureRandom);
            var publicKeyCertificate = publicKeyCertificateGenerator.Generate(signatureFactory);
            var certificate2 = new X509Certificate2(publicKeyCertificate.GetEncoded(), parameters.PublicCertificatePassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

            return certificate2;
        }

        public async Task UploadPrivateCertificateAsync(CertificateStorageType storageType, X509Certificate2 certificate, string password, string certificateName)
        {
            var storage = _certificateStorageFactory.Create(storageType);
            await storage.UploadCertificateAsync(certificate, password, certificateName);
        }

        public string GeneratePrivateCertificateName(string tenantId, string itemId) =>
            _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenantId}::{itemId}"));
    }
}
