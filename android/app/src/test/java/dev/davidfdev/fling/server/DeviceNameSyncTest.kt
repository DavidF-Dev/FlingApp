package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.http.HttpStatusCode
import io.ktor.server.testing.testApplication
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class DeviceNameSyncTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    private fun testDeviceRepository() = DeviceRepository(tempFolder.newFile("devices.json"))
    private val autoAcceptApprover = object : PairingApprover {
        override suspend fun requestApproval(deviceName: String) = true
    }

    private fun pairedRepo(name: String = "Old PC"): DeviceRepository {
        val repo = testDeviceRepository()
        runBlocking { repo.store(PairedDevice(name, "valid-key", 1000L)) }
        return repo
    }

    @Test
    fun nameHeaderUpdatesStoredName() {
        val repo = pairedRepo("Old PC")
        testApplication {
            application { configureFling("Phone", repo, autoAcceptApprover, ClipboardBuffer()) }

            val response = client.get("/ping") {
                header("X-Fling-Key", "valid-key")
                header("X-Fling-Name", "New PC")
            }
            assertEquals(HttpStatusCode.OK, response.status)
        }
        val updated = runBlocking { repo.findByKey("valid-key") }
        assertEquals("New PC", updated?.name)
    }

    @Test
    fun missingNameHeaderLeavesNameUnchanged() {
        val repo = pairedRepo("Old PC")
        testApplication {
            application { configureFling("Phone", repo, autoAcceptApprover, ClipboardBuffer()) }

            val response = client.get("/ping") {
                header("X-Fling-Key", "valid-key")
            }
            assertEquals(HttpStatusCode.OK, response.status)
        }
        val unchanged = runBlocking { repo.findByKey("valid-key") }
        assertEquals("Old PC", unchanged?.name)
    }

    @Test
    fun sameNameDoesNotTriggerWrite() {
        val repo = pairedRepo("Same PC")
        testApplication {
            application { configureFling("Phone", repo, autoAcceptApprover, ClipboardBuffer()) }

            val response = client.get("/ping") {
                header("X-Fling-Key", "valid-key")
                header("X-Fling-Name", "Same PC")
            }
            assertEquals(HttpStatusCode.OK, response.status)
        }
        val unchanged = runBlocking { repo.findByKey("valid-key") }
        assertEquals("Same PC", unchanged?.name)
    }
}
