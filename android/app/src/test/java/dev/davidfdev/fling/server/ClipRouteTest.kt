package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.pairing.PairingApprover
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
import java.io.ByteArrayOutputStream
import java.util.zip.GZIPOutputStream

class ClipRouteTest {

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

    private fun gzip(data: ByteArray): ByteArray {
        val bos = ByteArrayOutputStream()
        GZIPOutputStream(bos).use { it.write(data) }
        return bos.toByteArray()
    }

    private fun base64(data: ByteArray): String =
        java.util.Base64.getEncoder().encodeToString(data)

    @Test
    fun clipWithValidTextReturns200() = testApplication {
        val buffer = ClipboardBuffer()
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, buffer) }

        val encoded = base64("Hello World".toByteArray())
        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("""{"type":"text/plain","data":"$encoded","timestamp":1720100000}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<StatusResponse>(response.bodyAsText())
        assertEquals("ok", body.status)
        assertEquals("Phone", body.name)
        assertEquals(1, buffer.size())

        val item = buffer.getAll().first()
        assertEquals("text/plain", item.type)
        assertEquals("Hello World", String(item.data))
    }

    @Test
    fun clipWithValidImageReturns200() = testApplication {
        val buffer = ClipboardBuffer()
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, buffer) }

        val fakeImage = ByteArray(100) { it.toByte() }
        val encoded = base64(fakeImage)
        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("""{"type":"image/png","data":"$encoded","timestamp":1720100000}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)
        assertEquals(1, buffer.size())
    }

    @Test
    fun clipWithUnsupportedTypeReturns400() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("""{"type":"application/pdf","data":"abc","timestamp":1720100000}""")
        }
        assertEquals(HttpStatusCode.BadRequest, response.status)
    }

    @Test
    fun clipWithMalformedJsonReturns400() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("not json")
        }
        assertEquals(HttpStatusCode.BadRequest, response.status)
    }

    @Test
    fun clipWithMissingFieldsReturns400() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("""{"type":"text/plain"}""")
        }
        assertEquals(HttpStatusCode.BadRequest, response.status)
    }

    @Test
    fun clipWithOversizedPayloadReturns413() = testApplication {
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, ClipboardBuffer()) }

        val bigData = ByteArray(11 * 1024 * 1024)
        val encoded = base64(bigData)
        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("""{"type":"text/plain","data":"$encoded","timestamp":1720100000}""")
        }
        assertEquals(HttpStatusCode.PayloadTooLarge, response.status)
    }

    @Test
    fun clipWithGzipCompression() = testApplication {
        val buffer = ClipboardBuffer()
        application { configureFling({ "Phone" }, pairedRepo(), autoAcceptApprover, buffer) }

        val original = "Hello Compressed World"
        val compressed = gzip(original.toByteArray())
        val encoded = base64(compressed)
        val response = client.post("/clip") {
            header("X-Fling-Key", "valid-key")
            contentType(ContentType.Application.Json)
            setBody("""{"type":"text/plain","data":"$encoded","timestamp":1720100000,"compressed":true}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)

        val item = buffer.getAll().first()
        assertEquals(original, String(item.data))
    }
}
