package dev.davidfdev.fling.pairing

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import dev.davidfdev.fling.R
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeoutOrNull
import java.util.concurrent.atomic.AtomicReference

class NotificationPairingApprover(private val context: Context) : PairingApprover {

    private val pending = AtomicReference<CompletableDeferred<Boolean>?>(null)

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(ctx: Context, intent: Intent) {
            val accepted = intent.action == ACTION_ACCEPT
            pending.get()?.complete(accepted)
            dismissNotification()
        }
    }

    init {
        val filter = IntentFilter().apply {
            addAction(ACTION_ACCEPT)
            addAction(ACTION_REJECT)
        }
        ContextCompat.registerReceiver(context, receiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
        createChannel()
    }

    override suspend fun requestApproval(deviceName: String): Boolean {
        val deferred = CompletableDeferred<Boolean>()
        if (!pending.compareAndSet(null, deferred)) {
            return false
        }

        showNotification(deviceName)

        val result = withTimeoutOrNull(TIMEOUT_MS) { deferred.await() } ?: false

        pending.set(null)
        dismissNotification()
        return result
    }

    fun destroy() {
        context.unregisterReceiver(receiver)
        pending.get()?.complete(false)
        dismissNotification()
    }

    private fun createChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Pairing Requests",
            NotificationManager.IMPORTANCE_HIGH,
        )
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    private fun showNotification(deviceName: String) {
        val acceptIntent = PendingIntent.getBroadcast(
            context, 0,
            Intent(ACTION_ACCEPT).setPackage(context.packageName),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val rejectIntent = PendingIntent.getBroadcast(
            context, 1,
            Intent(ACTION_REJECT).setPackage(context.packageName),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setContentTitle("Pairing request")
            .setContentText("\"$deviceName\" wants to connect")
            .setSmallIcon(R.drawable.ic_notification)
            .addAction(0, "Accept", acceptIntent)
            .addAction(0, "Reject", rejectIntent)
            .setAutoCancel(false)
            .setOngoing(true)
            .build()

        context.getSystemService(NotificationManager::class.java)
            .notify(NOTIFICATION_ID, notification)
    }

    private fun dismissNotification() {
        context.getSystemService(NotificationManager::class.java).cancel(NOTIFICATION_ID)
    }

    companion object {
        const val CHANNEL_ID = "fling_pairing"
        const val NOTIFICATION_ID = 2
        const val TIMEOUT_MS = 30_000L
        private const val ACTION_ACCEPT = "dev.davidfdev.fling.PAIR_ACCEPT"
        private const val ACTION_REJECT = "dev.davidfdev.fling.PAIR_REJECT"
    }
}
