package dev.davidfdev.fling.server

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RateLimiterTest {

    @Test
    fun allowsUpToLimit() {
        val limiter = RateLimiter(maxRequests = 3, windowMs = 60_000)
        assertTrue(limiter.allow("key1"))
        assertTrue(limiter.allow("key1"))
        assertTrue(limiter.allow("key1"))
    }

    @Test
    fun rejectsBeyondLimit() {
        val limiter = RateLimiter(maxRequests = 3, windowMs = 60_000)
        repeat(3) { limiter.allow("key1") }
        assertFalse(limiter.allow("key1"))
    }

    @Test
    fun resetsAfterWindowExpires() {
        var now = 1000L
        val limiter = RateLimiter(maxRequests = 2, windowMs = 60_000, clock = { now })

        assertTrue(limiter.allow("key1"))
        assertTrue(limiter.allow("key1"))
        assertFalse(limiter.allow("key1"))

        now += 60_001
        assertTrue(limiter.allow("key1"))
    }

    @Test
    fun perKeyIsolation() {
        val limiter = RateLimiter(maxRequests = 2, windowMs = 60_000)
        assertTrue(limiter.allow("key1"))
        assertTrue(limiter.allow("key1"))
        assertFalse(limiter.allow("key1"))

        assertTrue(limiter.allow("key2"))
        assertTrue(limiter.allow("key2"))
    }

    @Test
    fun slidingWindowEvictsOldEntries() {
        var now = 1000L
        val limiter = RateLimiter(maxRequests = 2, windowMs = 60_000, clock = { now })

        assertTrue(limiter.allow("key1"))
        now += 30_000
        assertTrue(limiter.allow("key1"))
        assertFalse(limiter.allow("key1"))

        // First request should have expired, second still within window
        now += 31_000
        assertTrue(limiter.allow("key1"))
        assertFalse(limiter.allow("key1"))
    }
}
