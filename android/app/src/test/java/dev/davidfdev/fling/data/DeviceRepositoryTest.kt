package dev.davidfdev.fling.data

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class DeviceRepositoryTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    private fun createRepo() = DeviceRepository(tempFolder.newFile("devices.json"))

    @Test
    fun storeAndRetrieve() = runBlocking {
        val repo = createRepo()
        val device = PairedDevice(name = "Test PC", apiKey = "key1", pairedAt = 1000L)

        repo.store(device)

        val retrieved = repo.findByKey("key1")
        assertNotNull(retrieved)
        assertEquals("Test PC", retrieved!!.name)
        assertEquals("key1", retrieved.apiKey)
        assertEquals(1000L, retrieved.pairedAt)
    }

    @Test
    fun deleteRemovesDevice() = runBlocking {
        val repo = createRepo()
        repo.store(PairedDevice(name = "PC", apiKey = "key1", pairedAt = 1000L))

        repo.delete("key1")

        assertNull(repo.findByKey("key1"))
    }

    @Test
    fun idempotentStoreDoesNotDuplicate() = runBlocking {
        val repo = createRepo()
        val device = PairedDevice(name = "PC", apiKey = "key1", pairedAt = 1000L)

        repo.store(device)
        repo.store(device)

        assertEquals(1, repo.getAll().size)
    }

    @Test
    fun multipleDevicesStored() = runBlocking {
        val repo = createRepo()
        repo.store(PairedDevice(name = "PC1", apiKey = "key1", pairedAt = 1000L))
        repo.store(PairedDevice(name = "PC2", apiKey = "key2", pairedAt = 2000L))

        assertEquals(2, repo.getAll().size)
        assertNotNull(repo.findByKey("key1"))
        assertNotNull(repo.findByKey("key2"))
    }

    @Test
    fun emptyFileReturnsEmptyList() = runBlocking {
        val repo = createRepo()
        assertEquals(0, repo.getAll().size)
    }
}
