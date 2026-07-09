package dev.davidfdev.fling

import android.content.Context
import android.net.wifi.WifiManager
import android.util.Log
import dev.davidfdev.fling.data.SettingsRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class DiscoveryListener(
    private val context: Context,
    private val settingsRepository: SettingsRepository,
    private val scope: CoroutineScope,
) {

    private var socket: DatagramSocket? = null
    private var listenJob: Job? = null
    private var multicastLock: WifiManager.MulticastLock? = null

    fun start() {
        if (listenJob?.isActive == true) return

        val wifiManager = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        multicastLock = wifiManager.createMulticastLock("fling_discovery").apply {
            setReferenceCounted(false)
            acquire()
        }

        listenJob = scope.launch {
            try {
                val sock = DatagramSocket(DISCOVERY_PORT).also { socket = it }
                sock.reuseAddress = true
                val buffer = ByteArray(64)

                while (isActive) {
                    val packet = DatagramPacket(buffer, buffer.size)
                    withContext(Dispatchers.IO) {
                        sock.receive(packet)
                    }
                    val message = String(packet.data, packet.offset, packet.length).trim()
                    if (message == DISCOVERY_REQUEST) {
                        handleDiscoveryRequest(sock, packet.address, packet.port)
                    }
                }
            } catch (e: Exception) {
                if (isActive) {
                    Log.e(TAG, "Discovery listener error", e)
                }
            }
        }
        Log.i(TAG, "Discovery listener started on port $DISCOVERY_PORT")
    }

    fun stop() {
        listenJob?.cancel()
        listenJob = null
        socket?.close()
        socket = null
        multicastLock?.let {
            if (it.isHeld) it.release()
        }
        multicastLock = null
        Log.i(TAG, "Discovery listener stopped")
    }

    private suspend fun handleDiscoveryRequest(
        socket: DatagramSocket,
        address: InetAddress,
        port: Int,
    ) {
        try {
            val settings = settingsRepository.get()
            val deviceName = settings.deviceName.ifBlank { android.os.Build.MODEL }
            val response = "$DISCOVERY_RESPONSE_PREFIX${settings.port}:$deviceName"
            val responseBytes = response.toByteArray()
            val responsePacket = DatagramPacket(responseBytes, responseBytes.size, address, port)
            withContext(Dispatchers.IO) {
                socket.send(responsePacket)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to send discovery response", e)
        }
    }

    companion object {
        private const val TAG = "DiscoveryListener"
        const val DISCOVERY_PORT = 7290
        const val DISCOVERY_REQUEST = "FLING?"
        const val DISCOVERY_RESPONSE_PREFIX = "FLING:"
    }
}
