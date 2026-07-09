package dev.davidfdev.fling.server

import dev.davidfdev.fling.data.DeviceRepository
import io.ktor.http.HttpStatusCode
import io.ktor.server.application.createRouteScopedPlugin
import io.ktor.server.response.respond
import io.ktor.server.routing.Route

private val ApiKeyAuthPlugin = createRouteScopedPlugin(
    name = "ApiKeyAuth",
    createConfiguration = { ApiKeyAuthConfig() },
) {
    val repo = pluginConfig.deviceRepository
        ?: error("DeviceRepository must be provided to ApiKeyAuth plugin")

    onCall { call ->
        val key = call.request.headers["X-Fling-Key"]
        val device = if (key != null) repo.findByKey(key) else null
        if (device == null) {
            call.respond(HttpStatusCode.Unauthorized, ErrorResponse("unauthorized"))
            return@onCall
        }
        val newName = call.request.headers["X-Fling-Name"]
        if (newName != null && newName.isNotBlank() && newName != device.name) {
            repo.updateName(device.apiKey, newName)
        }
    }
}

class ApiKeyAuthConfig {
    var deviceRepository: DeviceRepository? = null
}

fun Route.authenticated(deviceRepository: DeviceRepository, build: Route.() -> Unit): Route {
    val route = createChild(AuthenticatedRouteSelector())
    route.install(ApiKeyAuthPlugin) {
        this.deviceRepository = deviceRepository
    }
    route.build()
    return route
}

private class AuthenticatedRouteSelector : io.ktor.server.routing.RouteSelector() {
    override suspend fun evaluate(
        context: io.ktor.server.routing.RoutingResolveContext,
        segmentIndex: Int,
    ) = io.ktor.server.routing.RouteSelectorEvaluation.Transparent

    override fun toString() = "(authenticated)"
}
