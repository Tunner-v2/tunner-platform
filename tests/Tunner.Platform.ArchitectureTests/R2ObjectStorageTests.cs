using System.Text;
using Tunner.Platform.ObjectStorage;
using Tunner.Platform.ObjectStorage.R2;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class R2ObjectStorageTests
{
    [Fact]
    public async Task DelegatesOpaqueObjectOperationsThroughTheInjectedR2Seam()
    {
        var client = new ProbeR2S3Client();
        var storage = new R2ObjectStorage(client);
        var address = new ObjectStorageAddress("test-bucket", "opaque-key");

        await using var writeContent = new MemoryStream(Encoding.UTF8.GetBytes("write-body"));
        await storage.PutAsync(new ObjectStorageWriteRequest(address, writeContent));
        Assert.Equal(address, client.WrittenAddress);
        Assert.Equal("write-body", Encoding.UTF8.GetString(client.WrittenContent));

        using var readResult = await storage.OpenReadAsync(address);
        using var reader = new StreamReader(readResult.Content, Encoding.UTF8, leaveOpen: true);
        Assert.Equal("read-body", await reader.ReadToEndAsync());

        await storage.DeleteAsync(address);
        Assert.Equal(address, client.DeletedAddress);
    }

    [Fact]
    public void ContractRejectsMissingTechnicalAddressValues()
    {
        Assert.Throws<ArgumentException>(() => new ObjectStorageAddress(" ", "key"));
        Assert.Throws<ArgumentException>(() => new ObjectStorageAddress("bucket", " "));
    }

    private sealed class ProbeR2S3Client : IR2S3Client
    {
        public ObjectStorageAddress? WrittenAddress { get; private set; }

        public byte[] WrittenContent { get; private set; } = [];

        public ObjectStorageAddress? DeletedAddress { get; private set; }

        public async Task PutAsync(ObjectStorageWriteRequest request, CancellationToken cancellationToken = default)
        {
            WrittenAddress = request.Address;
            await using var copy = new MemoryStream();
            await request.Content.CopyToAsync(copy, cancellationToken);
            WrittenContent = copy.ToArray();
        }

        public Task<ObjectStorageReadResult> OpenReadAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
        {
            var content = new MemoryStream(Encoding.UTF8.GetBytes("read-body"));
            return Task.FromResult(new ObjectStorageReadResult(content));
        }

        public Task DeleteAsync(ObjectStorageAddress address, CancellationToken cancellationToken = default)
        {
            DeletedAddress = address;
            return Task.CompletedTask;
        }
    }
}
