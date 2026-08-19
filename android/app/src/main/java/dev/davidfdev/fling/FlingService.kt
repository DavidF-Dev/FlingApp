package dev.davidfdev.fling

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import dev.davidfdev.fling.content.NotificationContentNotifier
import dev.davidfdev.fling.data.ClipImageCache
import dev.davidfdev.fling.pairing.NotificationPairingApprover
import dev.davidfdev.fling.server.RateLimiter
import dev.davidfdev.fling.server.configureFling
import io.ktor.server.engine.EmbeddedServer
import io.ktor.server.engine.embeddedServer
import io.ktor.server.netty.Netty
import io.ktor.server.netty.NettyApplicationEngine
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

class FlingService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var server: EmbeddedServer<NettyApplicationEngine, NettyApplicationEngine.Configuration>? = null
    private var pairingApprover: NotificationPairingApprover? = null
    private var discoveryListener: DiscoveryListener? = null
    private var wifiGateJob: Job? = null
    private var started = false

    private val app get() = application as FlingApplication

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        createNotificationChannel()
        startInForeground(buildNotification())
        if (!started) {
            started = true
            ClipImageCache.clear(this)
            startServer()
            startDiscovery()
        }
        app.setServiceRunning(true)
        return START_STICKY
    }

    private fun startServer() {
        val approver = NotificationPairingApprover(this).also { pairingApprover = it }
        val notifier = NotificationContentNotifier(this)

        scope.launch {
            try {
                val settings = app.settingsRepository.get()
                val port = settings.port
                server = embeddedServer(Netty, port = port, host = "0.0.0.0") {
                    configureFling(
                        { app.settingsRepository.get().deviceName.ifBlank { Build.MODEL } },
                        app.deviceRepository,
                        approver,
                        app.clipboardBuffer,
                        notifier,
                        RateLimiter(),
                    )
                }.also { it.start(wait = false) }
                Log.i(TAG, "Server started on port $port")
            } catch (e: Exception) {
                Log.e(TAG, "Failed to start server", e)
                stopSelf()
            }
        }
    }

    override fun onDestroy() {
        started = false
        wifiGateJob?.cancel()
        wifiGateJob = null
        discoveryListener?.stop()
        discoveryListener = null
        val serverToStop = server
        server = null
        pairingApprover?.destroy()
        pairingApprover = null
        scope.cancel()
        Thread {
            serverToStop?.stop(1000, 2000)
            app.setServiceRunning(false)
        }.start()
        super.onDestroy()
    }

    private fun startDiscovery() {
        val listener = DiscoveryListener(this, app.settingsRepository, scope)
            .also { discoveryListener = it }

        if (app.connectivityObserver.isWifiConnected.value) {
            listener.start()
        }

        wifiGateJob = scope.launch {
            app.connectivityObserver.isWifiConnected.collect { connected ->
                if (connected) {
                    listener.start()
                } else {
                    listener.stop()
                }
            }
        }
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Fling Service",
            NotificationManager.IMPORTANCE_LOW,
        )
        channel.setShowBadge(false)
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    private fun buildNotification(): Notification {
        val tapIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP
        }
        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            tapIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("Fling is running")
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .setVisibility(NotificationCompat.VISIBILITY_SECRET)
            .setSilent(true)
            .setShowWhen(false)
            .setOnlyAlertOnce(true)
            .build()
    }

    private fun startInForeground(notification: Notification) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE,
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    companion object {
        private const val TAG = "FlingService"
        const val CHANNEL_ID = "fling_service"
        const val NOTIFICATION_ID = 1
    }
}
