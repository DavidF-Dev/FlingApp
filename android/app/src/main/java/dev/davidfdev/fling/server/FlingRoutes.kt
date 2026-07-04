package dev.davidfdev.fling.server

import io.ktor.http.HttpStatusCode
import io.ktor.server.application.Application
import io.ktor.server.application.install
import io.ktor.server.plugins.contentnegotiation.ContentNegotiation
import io.ktor.server.response.respond
import io.ktor.server.routing.get
import io.ktor.server.routing.routing
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable
data class PingResponse(val status: String, val name: String, val version: String)

fun Application.configureFling(deviceName: String) {
    install(ContentNegotiation) {
        json(Json { encodeDefaults = true })
    }

    routing {
        get("/ping") {
            call.respond(PingResponse(status = "ok", name = deviceName, version = "1.0.0"))
        }
    }
}
