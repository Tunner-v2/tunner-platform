using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Tunner.Installer;
using Tunner.Platform.Configuration;
using Tunner.Platform.ObjectStorage;
using Tunner.Platform.Secrets;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class ConfiguredStorageTests
{
    [Fact]
    public async Task FactoryResolvesOpaqueReferenceAndConfiguresExistingAdapter()
    {
        var secrets = new RecordingSecrets();
        using var sdk = new RecordingS3();
        var invoked = false;
        using var configured = await ConfiguredR2Storage.CreateAsync(InstallerTests.Profile(), secrets, (credentials, config) =>
        {
            invoked = true;
            var actual = credentials.GetCredentials();
            Assert.Equal("fixture-id-value", actual.AccessKey);
            Assert.Equal("fixture-secret-value", actual.SecretKey);
            Assert.Equal(new Uri(InstallerTests.Profile().Endpoint), new Uri(config.ServiceURL));
            Assert.Equal("auto", config.AuthenticationRegion);
            Assert.True(config.ForcePathStyle);
            return sdk;
        });
        Assert.True(invoked);
        Assert.Equal(InstallerTests.Profile().CredentialResourceReference, secrets.Reference!.ResourceReference);
        Assert.Throws<ObjectDisposedException>(() => secrets.Values!.GetRequired("fixture-id"));
        using var content = new MemoryStream([1]);
        await configured.Storage.PutAsync(new ObjectStorageWriteRequest(new ObjectStorageAddress("fixture-test", "test"), content));
        Assert.NotNull(sdk.Put);
        Assert.True(sdk.Put.DisablePayloadSigning);
        Assert.True(sdk.Put.DisableDefaultChecksumValidation);
    }

    [Fact]
    public async Task MissingCredentialPreventsSdkConstruction()
    {
        var secrets = new RecordingSecrets { MissingField = true };
        var invoked = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfiguredR2Storage.CreateAsync(
            InstallerTests.Profile(), secrets, (_, _) => { invoked = true; return new RecordingS3(); }));
        Assert.False(invoked);
        Assert.Throws<ObjectDisposedException>(() => secrets.Values!.GetRequired("fixture-id"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task AbsenceRequiresActualNotFound(HttpStatusCode status, bool expected)
    {
        using var sdk = new RecordingS3 { Status = status };
        using var configured = await ConfiguredR2Storage.CreateAsync(InstallerTests.Profile(), new RecordingSecrets(), (_, _) => sdk);
        Assert.Equal(expected, await configured.IsAbsentAsync(new ObjectStorageAddress("fixture-test", "test"), CancellationToken.None));
    }

    [Fact]
    public async Task ForbiddenIsNotEvidenceOfDeletion()
    {
        using var sdk = new RecordingS3 { Status = HttpStatusCode.Forbidden };
        using var configured = await ConfiguredR2Storage.CreateAsync(InstallerTests.Profile(), new RecordingSecrets(), (_, _) => sdk);
        await Assert.ThrowsAsync<AmazonS3Exception>(() => configured.IsAbsentAsync(new ObjectStorageAddress("fixture-test", "test"), CancellationToken.None));
    }

    private sealed class RecordingSecrets : ISecretStore
    {
        public bool MissingField { get; init; }
        public EngineeringSecretReference? Reference { get; private set; }
        public SecretValues? Values { get; private set; }

        public Task<SecretValues> ReadAsync(EngineeringSecretReference reference, CancellationToken cancellationToken = default)
        {
            Reference = reference;
            var data = new Dictionary<string, string> { ["fixture-id"] = "fixture-id-value" };
            if (!MissingField) data.Add("fixture-secret", "fixture-secret-value");
            Values = new SecretValues(data);
            return Task.FromResult(Values);
        }
    }

    private sealed class RecordingS3() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public PutObjectRequest? Put { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Put = request;
            return Task.FromResult(new PutObjectResponse());
        }

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
        {
            if (Status != HttpStatusCode.OK) throw new AmazonS3Exception("fixture") { StatusCode = Status };
            return Task.FromResult(new GetObjectMetadataResponse { HttpStatusCode = Status });
        }
    }
}
