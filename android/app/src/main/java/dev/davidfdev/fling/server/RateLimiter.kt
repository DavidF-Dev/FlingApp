package dev.davidfdev.fling.server

import java.util.LinkedList
import java.util.concurrent.ConcurrentHashMap

class RateLimiter(
    private val maxRequests: Int = 10,
    private val windowMs: Long = 60_000,
    private val clock: () -> Long = System::currentTimeMillis,
) {

    private val requests = ConcurrentHashMap<String, LinkedList<Long>>()

    fun allow(key: String): Boolean {
        val now = clock()
        val timestamps = requests.computeIfAbsent(key) { LinkedList() }
        synchronized(timestamps) {
            while (timestamps.isNotEmpty() && timestamps.first() < now - windowMs) {
                timestamps.removeFirst()
            }
            if (timestamps.size >= maxRequests) return false
            timestamps.add(now)
            return true
        }
    }
}
