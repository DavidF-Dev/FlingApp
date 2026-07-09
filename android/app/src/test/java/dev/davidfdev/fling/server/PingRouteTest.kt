package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.statement.bodyAsText
import io.ktor.http.HttpStatusCode
import io.ktor.server.testing.testApplication
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class PingRouteTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    private fun testDeviceRepository() = DeviceRepository(tempFolder.newFile("devices.json"))
    private val autoAcceptApprover = object : PairingApprover {
        override suspend fun requestApproval(deviceName: String) = true
    }

    private fun pairedRepo(): DeviceRepository {
        val repo = testDeviceRepository()
        runBlocking { repo.store(PairedDevice("PC", "valid-key", 1000L)) }
        return repo
    }

    @Test
    fun pingWithValidKeyReturns200() = testApplication {
        application { configureFling({ "Test Device" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.get("/ping") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<PingResponse>(response.bodyAsText())
        assertEquals("ok", body.status)
        assertEquals("Test Device", body.name)
        assertEquals("1.0.0", body.version)
    }

    @Test
    fun unknownRouteReturns404() = testApplication {
        application { configureFling({ "Test Device" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.get("/nonexistent") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.NotFound, response.status)
    }
}
