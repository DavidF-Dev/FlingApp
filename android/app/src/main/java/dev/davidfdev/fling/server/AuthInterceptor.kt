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
        if (key == null || repo.findByKey(key) == null) {
            call.respond(HttpStatusCode.Unauthorized, ErrorResponse("unauthorized"))
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
