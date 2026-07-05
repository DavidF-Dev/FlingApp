package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.DeviceRepository
import dev.davidfdev.fling.data.PairedDevice
import dev.davidfdev.fling.pairing.PairingApprover
import io.ktor.http.HttpStatusCode
import io.ktor.server.application.Application
import io.ktor.server.application.install
import io.ktor.server.plugins.contentnegotiation.ContentNegotiation
import io.ktor.server.request.receive
import io.ktor.server.response.respond
import io.ktor.server.routing.get
import io.ktor.server.routing.post
import io.ktor.server.routing.routing
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.util.concurrent.atomic.AtomicBoolean

@Serializable
data class PingResponse(val status: String, val name: String, val version: String)

@Serializable
data class PairRequest(val name: String? = null, val key: String? = null)

@Serializable
data class PairResponse(val status: String, val name: String? = null)

@Serializable
data class ErrorResponse(val error: String)

fun Application.configureFling(
    deviceName: String,
    deviceRepository: DeviceRepository,
    pairingApprover: PairingApprover,
) {
    install(ContentNegotiation) {
        json(Json { encodeDefaults = true })
    }

    val pairingInProgress = AtomicBoolean(false)

    routing {
        post("/pair") {
            val request = try {
                call.receive<PairRequest>()
            } catch (_: Exception) {
                call.respond(HttpStatusCode.BadRequest, ErrorResponse("invalid request body"))
                return@post
            }

            if (request.name.isNullOrBlank() || request.key.isNullOrBlank()) {
                call.respond(HttpStatusCode.BadRequest, ErrorResponse("name and key are required"))
                return@post
            }

            val existing = deviceRepository.findByKey(request.key)
            if (existing != null) {
                call.respond(PairResponse(status = "accepted", name = deviceName))
                return@post
            }

            if (!pairingInProgress.compareAndSet(false, true)) {
                call.respond(PairResponse(status = "rejected"))
                return@post
            }

            try {
                val approved = pairingApprover.requestApproval(request.name)
                if (approved) {
                    val device = PairedDevice(
                        name = request.name,
                        apiKey = request.key,
                        pairedAt = System.currentTimeMillis(),
                    )
                    deviceRepository.store(device)
                    call.respond(PairResponse(status = "accepted", name = deviceName))
                } else {
                    call.respond(PairResponse(status = "rejected"))
                }
            } finally {
                pairingInProgress.set(false)
            }
        }

        authenticated(deviceRepository) {
            get("/ping") {
                call.respond(PingResponse(status = "ok", name = deviceName, version = "1.0.0"))
            }
        }
    }
}
