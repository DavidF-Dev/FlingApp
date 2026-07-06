package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.ClipboardBuffer
import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.server.testing.testApplication
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class RateLimitRouteTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    private fun testDeviceRepository() = DeviceRepository(tempFolder.newFile("devices.json"))
    private val autoAcceptApprover = object : PairingApprover {
        override suspend fun requestApproval(deviceName: String) = true
    }

    private fun pairedRepo(): DeviceRepository {
        val repo = testDeviceRepository()
        runBlocking { repo.store(PairedDevice("PC", "valid-key", 1000L)) }
        runBlocking { repo.store(PairedDevice("PC2", "other-key", 1000L)) }
        return repo
    }

    @Test
    fun requestsWithinLimitSucceed() = testApplication {
        val limiter = RateLimiter(maxRequests = 3, windowMs = 60_000)
        application { configureFling("Phone", pairedRepo(), autoAcceptApprover, ClipboardBuffer(), rateLimiter = limiter) }

        repeat(3) {
            val response = client.get("/ping") { header("X-Fling-Key", "valid-key") }
            assertEquals(HttpStatusCode.OK, response.status)
        }
    }

    @Test
    fun requestsBeyondLimitReturn429() = testApplication {
        val limiter = RateLimiter(maxRequests = 2, windowMs = 60_000)
        application { configureFling("Phone", pairedRepo(), autoAcceptApprover, ClipboardBuffer(), rateLimiter = limiter) }

        repeat(2) {
            client.get("/ping") { header("X-Fling-Key", "valid-key") }
        }

        val response = client.get("/ping") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.TooManyRequests, response.status)
    }

    @Test
    fun perKeyIsolation() = testApplication {
        val limiter = RateLimiter(maxRequests = 2, windowMs = 60_000)
        application { configureFling("Phone", pairedRepo(), autoAcceptApprover, ClipboardBuffer(), rateLimiter = limiter) }

        repeat(2) {
            client.get("/ping") { header("X-Fling-Key", "valid-key") }
        }
        val blocked = client.get("/ping") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.TooManyRequests, blocked.status)

        val otherKey = client.get("/ping") { header("X-Fling-Key", "other-key") }
        assertEquals(HttpStatusCode.OK, otherKey.status)
    }

    @Test
    fun pairNotRateLimited() = testApplication {
        val limiter = RateLimiter(maxRequests = 1, windowMs = 60_000)
        application { configureFling("Phone", pairedRepo(), autoAcceptApprover, ClipboardBuffer(), rateLimiter = limiter) }

        // Pair requests should not be affected by rate limiting
        repeat(3) {
            val response = client.post("/pair") {
                contentType(ContentType.Application.Json)
                setBody("""{"name":"PC","key":"valid-key"}""")
            }
            assertEquals(HttpStatusCode.OK, response.status)
        }
    }

    @Test
    fun resetsAfterWindowExpires() = testApplication {
        var now = 1000L
        val limiter = RateLimiter(maxRequests = 2, windowMs = 60_000, clock = { now })
        application { configureFling("Phone", pairedRepo(), autoAcceptApprover, ClipboardBuffer(), rateLimiter = limiter) }

        repeat(2) {
            client.get("/ping") { header("X-Fling-Key", "valid-key") }
        }
        val blocked = client.get("/ping") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.TooManyRequests, blocked.status)

        now += 60_001
        val allowed = client.get("/ping") { header("X-Fling-Key", "valid-key") }
        assertEquals(HttpStatusCode.OK, allowed.status)
    }
}
