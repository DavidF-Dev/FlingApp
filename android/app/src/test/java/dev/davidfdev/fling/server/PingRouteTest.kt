package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.client.request.get
import io.ktor.client.statement.bodyAsText
import io.ktor.http.HttpStatusCode
import io.ktor.server.testing.testApplication
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

    @Test
    fun pingReturns200WithCorrectShape() = testApplication {
        application { configureFling("Test Device", testDeviceRepository(), autoAcceptApprover) }

        val response = client.get("/ping")
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<PingResponse>(response.bodyAsText())
        assertEquals("ok", body.status)
        assertEquals("Test Device", body.name)
        assertEquals("1.0.0", body.version)
    }

    @Test
    fun unknownRouteReturns404() = testApplication {
        application { configureFling("Test Device", testDeviceRepository(), autoAcceptApprover) }

        val response = client.get("/nonexistent")
        assertEquals(HttpStatusCode.NotFound, response.status)
    }
}
