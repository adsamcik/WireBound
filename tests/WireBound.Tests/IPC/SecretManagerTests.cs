using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for SecretManager — generation, file I/O, permissions, and lifecycle.
/// Note: Tests that write to the secret file path may conflict with a running
/// WireBound instance. Tests are designed to be resilient to this.
/// </summary>
[NotInParallel("SecretFile")]
public class SecretManagerTests
{
    [Test]
    public void GetSecretFilePath_ReturnsPathWithWireBoundDirectory()
    {
        var path = SecretManager.GetSecretFilePath();

        path.Should().Contain("WireBound");
        path.Should().EndWith(".elevation-secret");
    }

    [Test]
    public void GetSecretFilePath_IsConsistentAcrossCalls()
    {
        var path1 = SecretManager.GetSecretFilePath();
        var path2 = SecretManager.GetSecretFilePath();
        path1.Should().Be(path2);
    }

    [Test]
    public void GetSecretFilePath_UsesLocalAppData()
    {
        var path = SecretManager.GetSecretFilePath();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        path.Should().StartWith(appData);
    }

    [Test]
    public void GenerateAndStore_Creates32ByteSecret()
    {
        byte[]? secret = null;
        try
        {
            secret = SecretManager.GenerateAndStore();
            secret.Should().NotBeNull();
            secret.Should().HaveCount(32);
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
        finally
        {
            try { SecretManager.Delete(); } catch { /* best effort */ }
        }
    }

    [Test]
    public void GenerateAndStore_ProducesUniqueSecrets()
    {
        byte[]? secret1 = null, secret2 = null;
        try
        {
            secret1 = SecretManager.GenerateAndStore();
            secret2 = SecretManager.GenerateAndStore();
            secret1.Should().NotBeEquivalentTo(secret2, "cryptographically random");
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
        finally
        {
            try { SecretManager.Delete(); } catch { /* best effort */ }
        }
    }

    [Test]
    public void GenerateAndStore_WritesFileToExpectedPath()
    {
        try
        {
            SecretManager.GenerateAndStore();
            var path = SecretManager.GetSecretFilePath();
            File.Exists(path).Should().BeTrue("secret file should be created");
            File.ReadAllBytes(path).Should().HaveCount(32);
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
        finally
        {
            try { SecretManager.Delete(); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Load_AfterGenerate_ReturnsSameSecret()
    {
        try
        {
            var generated = SecretManager.GenerateAndStore();
            var loaded = SecretManager.Load();
            loaded.Should().NotBeNull();
            loaded.Should().BeEquivalentTo(generated);
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
        finally
        {
            try { SecretManager.Delete(); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Load_WhenFileDoesNotExist_ReturnsNull()
    {
        // Use a temporary override approach — check behavior with nonexistent file
        // We can verify the null return by checking when file doesn't exist
        var path = SecretManager.GetSecretFilePath();
        if (File.Exists(path))
        {
            // File exists (maybe from running app), so test the normal load
            var result = SecretManager.Load();
            // If file has correct size, should return non-null; otherwise null
            if (new FileInfo(path).Length == 32)
                result.Should().NotBeNull();
            else
                result.Should().BeNull();
            return;
        }

        SecretManager.Load().Should().BeNull("file does not exist");
    }

    [Test]
    public void Load_WhenFileHasWrongSize_ReturnsNull()
    {
        var path = SecretManager.GetSecretFilePath();
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Create a backup if file exists
        byte[]? backup = null;
        try
        {
            if (File.Exists(path))
                backup = File.ReadAllBytes(path);

            // Write a file that's too short
            File.WriteAllBytes(path, new byte[16]);
            SecretManager.Load().Should().BeNull("16 bytes != 32 bytes");

            // Write a file that's too long
            File.WriteAllBytes(path, new byte[64]);
            SecretManager.Load().Should().BeNull("64 bytes != 32 bytes");
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
        finally
        {
            // Restore backup
            try
            {
                if (backup != null)
                    File.WriteAllBytes(path, backup);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* best effort */ }
        }
    }

    [Test]
    public void Delete_RemovesFile()
    {
        try
        {
            SecretManager.GenerateAndStore();
            var path = SecretManager.GetSecretFilePath();
            File.Exists(path).Should().BeTrue();

            SecretManager.Delete();
            File.Exists(path).Should().BeFalse();
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
    }

    [Test]
    public void Delete_OverwritesFileWithZerosBeforeDeleting()
    {
        FileStream? readHandle = null;
        try
        {
            SecretManager.GenerateAndStore();
            var path = SecretManager.GetSecretFilePath();

            // Confirm the stored secret contains non-zero data
            File.ReadAllBytes(path).Should().NotBeEquivalentTo(new byte[32]);

            // Keep a shared read handle open so we can inspect content after Delete
            // overwrites the file but before the OS fully removes it
            readHandle = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            SecretManager.Delete();

            // The file has been overwritten with zeros and marked for deletion;
            // our handle still points at the data
            readHandle.Seek(0, SeekOrigin.Begin);
            var buffer = new byte[32];
            Array.Fill(buffer, (byte)0xFF);
            readHandle.ReadExactly(buffer);

            buffer.Should().BeEquivalentTo(new byte[32],
                "secret should be zero-wiped before deletion");
        }
        catch (IOException ex)
        {
            Skip.Test($"Secret file locked by running WireBound instance: {ex.Message}");
        }
        finally
        {
            readHandle?.Dispose();
            try { SecretManager.Delete(); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Delete_WhenFileDoesNotExist_DoesNotThrow()
    {
        var path = SecretManager.GetSecretFilePath();
        Skip.When(File.Exists(path), "Can't safely delete if running app has it locked");

        var act = () => SecretManager.Delete();
        act.Should().NotThrow();
    }
}

