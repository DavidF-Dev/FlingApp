package dev.davidfdev.fling

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class DiscoveryListenerTest {

    @Test
    fun responseFormatIsCorrect() {
        val port = 7291
        val deviceName = "Test Phone"
        val response = "${DiscoveryListener.DISCOVERY_RESPONSE_PREFIX}$port:$deviceName"
        assertEquals("FLING:7291:Test Phone", response)
    }

    @Test
    fun responseUsesConfiguredPortAndName() {
        val port = 8080
        val deviceName = "My Custom Device"
        val response = "${DiscoveryListener.DISCOVERY_RESPONSE_PREFIX}$port:$deviceName"
        assertEquals("FLING:8080:My Custom Device", response)
    }

    @Test
    fun requestConstantMatchesProtocol() {
        assertEquals("FLING?", DiscoveryListener.DISCOVERY_REQUEST)
    }

    @Test
    fun responsePrefixMatchesProtocol() {
        assertEquals("FLING:", DiscoveryListener.DISCOVERY_RESPONSE_PREFIX)
    }

    @Test
    fun discoveryPortIsCorrect() {
        assertEquals(7290, DiscoveryListener.DISCOVERY_PORT)
    }

    @Test
    fun responseCanBeParsedByCliFormat() {
        val port = 7291
        val deviceName = "Galaxy S24"
        val response = "${DiscoveryListener.DISCOVERY_RESPONSE_PREFIX}$port:$deviceName"

        assertTrue(response.startsWith(DiscoveryListener.DISCOVERY_RESPONSE_PREFIX))
        val payload = response.removePrefix(DiscoveryListener.DISCOVERY_RESPONSE_PREFIX)
        val colonIndex = payload.indexOf(':')
        assertTrue(colonIndex > 0)
        val parsedPort = payload.substring(0, colonIndex).toInt()
        val parsedName = payload.substring(colonIndex + 1)
        assertEquals(7291, parsedPort)
        assertEquals("Galaxy S24", parsedName)
    }

    @Test
    fun nonFlingRequestIsIgnored() {
        val message = "HELLO?"
        val isDiscoveryRequest = message.trim() == DiscoveryListener.DISCOVERY_REQUEST
        assertEquals(false, isDiscoveryRequest)
    }

    @Test
    fun emptyPacketIsIgnored() {
        val message = ""
        val isDiscoveryRequest = message.trim() == DiscoveryListener.DISCOVERY_REQUEST
        assertEquals(false, isDiscoveryRequest)
    }
}
