package dev.davidfdev.fling.server

import io.ktor.http.HttpStatusCode
import io.ktor.server.application.createRouteScopedPlugin
import io.ktor.server.response.respond
import io.ktor.server.routing.Route
import io.ktor.server.routing.RouteSelector
import io.ktor.server.routing.RouteSelectorEvaluation
import io.ktor.server.routing.RoutingResolveContext

private val RateLimitPlugin = createRouteScopedPlugin(
    name = "RateLimit",
    createConfiguration = { RateLimitConfig() },
) {
    val limiter = pluginConfig.rateLimiter
        ?: error("RateLimiter must be provided to RateLimit plugin")

    onCall { call ->
        val key = call.request.headers["X-Fling-Key"] ?: return@onCall
        if (!limiter.allow(key)) {
            call.respond(HttpStatusCode.TooManyRequests, ErrorResponse("rate_limited"))
        }
    }
}

class RateLimitConfig {
    var rateLimiter: RateLimiter? = null
}

fun Route.rateLimited(rateLimiter: RateLimiter, build: Route.() -> Unit): Route {
    val route = createChild(RateLimitedRouteSelector())
    route.install(RateLimitPlugin) {
        this.rateLimiter = rateLimiter
    }
    route.build()
    return route
}

private class RateLimitedRouteSelector : RouteSelector() {
    override suspend fun evaluate(
        context: RoutingResolveContext,
        segmentIndex: Int,
    ) = RouteSelectorEvaluation.Transparent

    override fun toString() = "(rate-limited)"
}
