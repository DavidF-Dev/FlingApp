package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.server.testing.testApplication
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class AuthTest {

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
    fun pingWithoutHeaderReturns401() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.get("/ping")
        assertEquals(HttpStatusCode.Unauthorized, response.status)

        val body = Json.decodeFromString<ErrorResponse>(response.bodyAsText())
        assertEquals("unauthorized", body.error)
    }

    @Test
    fun pingWithUnknownKeyReturns401() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.get("/ping") { header("X-Fling-Key", "wrong-key") }
        assertEquals(HttpStatusCode.Unauthorized, response.status)

        val body = Json.decodeFromString<ErrorResponse>(response.bodyAsText())
        assertEquals("unauthorized", body.error)
    }

    @Test
    fun pingWithValidKeyReturns200() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.get("/ping") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.OK, response.status)
    }

    @Test
    fun pairNotAffectedByAuth() = testApplication {
        application { configureFling({ "Phone" }, testDeviceRepository(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"PC","key":"new-key"}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<PairResponse>(response.bodyAsText())
        assertEquals("accepted", body.status)
    }
}
