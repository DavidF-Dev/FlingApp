package dev.davidfdev.fling.server

import io.ktor.client.request.get
import io.ktor.client.statement.bodyAsText
import io.ktor.http.HttpStatusCode
import io.ktor.server.testing.testApplication
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Test

class PingRouteTest {

    @Test
    fun pingReturns200WithCorrectShape() = testApplication {
        application { configureFling("Test Device") }

        val response = client.get("/ping")
        assertEquals(HttpStatusCode.OK, response.status)

        val body = Json.decodeFromString<PingResponse>(response.bodyAsText())
        assertEquals("ok", body.status)
        assertEquals("Test Device", body.name)
        assertEquals("1.0.0", body.version)
    }

    @Test
    fun unknownRouteReturns404() = testApplication {
        application { configureFling("Test Device") }

        val response = client.get("/nonexistent")
        assertEquals(HttpStatusCode.NotFound, response.status)
    }
}
