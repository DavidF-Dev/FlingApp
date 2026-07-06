package dev.davidfdev.fling

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class ConnectivityObserver(context: Context) {

    private val connectivityManager =
        context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager

    private val _isWifiConnected = MutableStateFlow(checkWifi())
    val isWifiConnected: StateFlow<Boolean> = _isWifiConnected.asStateFlow()

    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            _isWifiConnected.value = checkWifi()
        }

        override fun onLost(network: Network) {
            _isWifiConnected.value = false
        }

        override fun onCapabilitiesChanged(
            network: Network,
            capabilities: NetworkCapabilities,
        ) {
            _isWifiConnected.value =
                capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
        }
    }

    init {
        connectivityManager.registerDefaultNetworkCallback(callback)
    }

    fun destroy() {
        connectivityManager.unregisterNetworkCallback(callback)
    }

    private fun checkWifi(): Boolean {
        val network = connectivityManager.activeNetwork ?: return false
        val caps = connectivityManager.getNetworkCapabilities(network) ?: return false
        return caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
    }
}
