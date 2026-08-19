package dev.davidfdev.fling.server

import dev.davidfdev.fling.BuildConfig
import dev.davidfdev.fling.content.ContentNotifier
import dev.davidfdev.fling.data.ClipItem
import dev.davidfdev.fling.data.ClipboardBuffer
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
import java.io.ByteArrayInputStream
import java.util.concurrent.atomic.AtomicBoolean
import java.util.zip.GZIPInputStream

private val SUPPORTED_TYPES = setOf("text/plain", "text/html", "image/png")
private const val MAX_DECODED_BYTES = 10 * 1024 * 1024

@Serializable
data class PingResponse(val status: String, val name: String, val version: String)

@Serializable
data class PairRequest(val name: String? = null, val key: String? = null)

@Serializable
data class PairResponse(val status: String, val name: String? = null)

@Serializable
data class ClipRequest(
    val type: String? = null,
    val data: String? = null,
    val timestamp: Long? = null,
    val compressed: Boolean = false,
)

@Serializable
data class StatusResponse(val status: String, val name: String? = null)

@Serializable
data class ErrorResponse(val error: String)

fun Application.configureFling(
    deviceNameProvider: suspend () -> String,
    deviceRepository: DeviceRepository,
    pairingApprover: PairingApprover,
    clipboardBuffer: ClipboardBuffer,
    contentNotifier: ContentNotifier? = null,
    rateLimiter: RateLimiter? = null,
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
                call.respond(PairResponse(status = "accepted", name = deviceNameProvider()))
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
                    call.respond(PairResponse(status = "accepted", name = deviceNameProvider()))
                } else {
                    call.respond(PairResponse(status = "rejected"))
                }
            } finally {
                pairingInProgress.set(false)
            }
        }

        authenticated(deviceRepository) {
            rateLimited(rateLimiter ?: RateLimiter()) {
                get("/ping") {
                    call.respond(
                        PingResponse(
                            status = "ok",
                            name = deviceNameProvider(),
                            version = BuildConfig.VERSION_NAME,
                        ),
                    )
                }

                post("/clip") {
                    val request = try {
                        call.receive<ClipRequest>()
                    } catch (_: Exception) {
                        call.respond(HttpStatusCode.BadRequest, ErrorResponse("invalid request body"))
                        return@post
                    }

                    if (request.type.isNullOrBlank() || request.data.isNullOrBlank()) {
                        call.respond(HttpStatusCode.BadRequest, ErrorResponse("type and data are required"))
                        return@post
                    }

                    if (request.type !in SUPPORTED_TYPES) {
                        call.respond(HttpStatusCode.BadRequest, ErrorResponse("unsupported type: ${request.type}"))
                        return@post
                    }

                    val rawBytes = try {
                        java.util.Base64.getDecoder().decode(request.data)
                    } catch (_: Exception) {
                        call.respond(HttpStatusCode.BadRequest, ErrorResponse("invalid base64 data"))
                        return@post
                    }

                    val decoded = if (request.compressed) {
                        try {
                            GZIPInputStream(ByteArrayInputStream(rawBytes)).readBytes()
                        } catch (_: Exception) {
                            call.respond(HttpStatusCode.BadRequest, ErrorResponse("invalid gzip data"))
                            return@post
                        }
                    } else {
                        rawBytes
                    }

                    if (decoded.size > MAX_DECODED_BYTES) {
                        call.respond(HttpStatusCode.PayloadTooLarge, ErrorResponse("payload exceeds 10 MB"))
                        return@post
                    }

                    val item = ClipItem(
                        type = request.type,
                        data = decoded,
                        timestamp = request.timestamp ?: System.currentTimeMillis(),
                        receivedAt = System.currentTimeMillis(),
                    )
                    clipboardBuffer.add(item)
                    contentNotifier?.notify(item)

                    call.respond(StatusResponse(status = "ok", name = deviceNameProvider()))
                }
            }
        }
    }
}
