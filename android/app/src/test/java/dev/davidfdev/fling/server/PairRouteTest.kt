package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.server.testing.testApplication
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class PairRouteTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    private fun testDeviceRepository() = DeviceRepository(tempFolder.newFile("devices.json"))

    private val autoAcceptApprover = object : PairingApprover {
        override suspend fun requestApproval(deviceName: String) = true
    }

    private val autoRejectApprover = object : PairingApprover {
        override suspend fun requestApproval(deviceName: String) = false
    }

    @Test
    fun pairWithValidBodyReturnsAccepted() = testApplication {
        val repo = testDeviceRepository()
        application { configureFling("Phone", repo, autoAcceptApprover) }

        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"Test PC","key":"abc123"}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<PairResponse>(response.bodyAsText())
        assertEquals("accepted", body.status)
        assertEquals("Phone", body.name)
    }

    @Test
    fun pairWithMissingNameReturns400() = testApplication {
        application { configureFling("Phone", testDeviceRepository(), autoAcceptApprover) }

        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"key":"abc123"}""")
        }
        assertEquals(HttpStatusCode.BadRequest, response.status)
    }

    @Test
    fun pairWithMissingKeyReturns400() = testApplication {
        application { configureFling("Phone", testDeviceRepository(), autoAcceptApprover) }

        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"Test PC"}""")
        }
        assertEquals(HttpStatusCode.BadRequest, response.status)
    }

    @Test
    fun pairWithMalformedJsonReturns400() = testApplication {
        application { configureFling("Phone", testDeviceRepository(), autoAcceptApprover) }

        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""not json""")
        }
        assertEquals(HttpStatusCode.BadRequest, response.status)
    }

    @Test
    fun pairIdempotentRePairAcceptsImmediately() = testApplication {
        val repo = testDeviceRepository()
        application { configureFling("Phone", repo, autoAcceptApprover) }

        // First pair
        client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"Test PC","key":"abc123"}""")
        }

        // Second pair with same key — accepted without approval
        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"Test PC","key":"abc123"}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)
        val body = Json.decodeFromString<PairResponse>(response.bodyAsText())
        assertEquals("accepted", body.status)

        // Only one device stored
        val devices = runBlocking { repo.getAll() }
        assertEquals(1, devices.size)
    }

    @Test
    fun pairRejectedWhenApproverRejects() = testApplication {
        application { configureFling("Phone", testDeviceRepository(), autoRejectApprover) }

        val response = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"Test PC","key":"abc123"}""")
        }
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<PairResponse>(response.bodyAsText())
        assertEquals("rejected", body.status)
        assertNull(body.name)
    }

    @Test
    fun concurrentPairRequestIsRejected() = testApplication {
        val gate = CompletableDeferred<Boolean>()
        val blockingApprover = object : PairingApprover {
            override suspend fun requestApproval(deviceName: String): Boolean = gate.await()
        }
        application { configureFling("Phone", testDeviceRepository(), blockingApprover) }

        // First request blocks on approval — fire and forget via externalServices isn't needed;
        // we use a coroutine scope from the test framework.
        val first = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.Default).launch {
            client.post("/pair") {
                contentType(ContentType.Application.Json)
                setBody("""{"name":"PC1","key":"key1"}""")
            }
        }

        // Give the first request time to reach the approver
        Thread.sleep(200)

        // Second request should be rejected immediately
        val second = client.post("/pair") {
            contentType(ContentType.Application.Json)
            setBody("""{"name":"PC2","key":"key2"}""")
        }
        val body = Json.decodeFromString<PairResponse>(second.bodyAsText())
        assertEquals("rejected", body.status)

        // Unblock the first and wait for completion
        gate.complete(true)
        runBlocking { first.join() }
    }
}
