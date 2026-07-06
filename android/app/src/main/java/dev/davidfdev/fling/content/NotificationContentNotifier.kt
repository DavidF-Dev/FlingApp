package dev.davidfdev.fling.content

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.BitmapFactory
import android.widget.Toast
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import dev.davidfdev.fling.R
import dev.davidfdev.fling.data.ClipItem
import java.io.File
import java.util.concurrent.atomic.AtomicInteger

class NotificationContentNotifier(private val context: Context) : ContentNotifier {

    private val nextId = AtomicInteger(FIRST_NOTIFICATION_ID)

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(ctx: Context, intent: Intent) {
            if (intent.action != ACTION_COPY) return

            val notificationId = intent.getIntExtra(EXTRA_NOTIFICATION_ID, -1)
            val type = intent.getStringExtra(EXTRA_TYPE) ?: return

            when {
                type.startsWith("text/") -> {
                    val text = intent.getStringExtra(EXTRA_TEXT) ?: return
                    val clip = android.content.ClipData.newPlainText("Fling", text)
                    ctx.getSystemService(android.content.ClipboardManager::class.java)
                        .setPrimaryClip(clip)
                }
                type == "image/png" -> {
                    val uriString = intent.getStringExtra(EXTRA_URI) ?: return
                    val uri = android.net.Uri.parse(uriString)
                    val clip = android.content.ClipData.newUri(ctx.contentResolver, "Fling", uri)
                    ctx.getSystemService(android.content.ClipboardManager::class.java)
                        .setPrimaryClip(clip)
                }
            }

            Toast.makeText(ctx, "Copied to clipboard", Toast.LENGTH_SHORT).show()

            if (notificationId != -1) {
                ctx.getSystemService(NotificationManager::class.java).cancel(notificationId)
            }
        }
    }

    init {
        val filter = IntentFilter(ACTION_COPY)
        ContextCompat.registerReceiver(context, receiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
        createChannel()
    }

    override fun notify(item: ClipItem) {
        val notificationId = nextId.getAndIncrement()

        val copyIntent = Intent(ACTION_COPY).apply {
            setPackage(context.packageName)
            putExtra(EXTRA_NOTIFICATION_ID, notificationId)
            putExtra(EXTRA_TYPE, item.type)
        }

        when {
            item.type.startsWith("text/") -> {
                val text = String(item.data)
                copyIntent.putExtra(EXTRA_TEXT, text)
                showTextNotification(notificationId, text, copyIntent)
            }
            item.type == "image/png" -> {
                val uri = writeImageToCache(item.data, notificationId)
                copyIntent.putExtra(EXTRA_URI, uri.toString())
                showImageNotification(notificationId, item.data, copyIntent)
            }
        }
    }

    fun destroy() {
        context.unregisterReceiver(receiver)
    }

    private fun showTextNotification(id: Int, text: String, copyIntent: Intent) {
        val preview = if (text.length > 100) text.take(100) + "…" else text

        val pendingIntent = PendingIntent.getBroadcast(
            context, id, copyIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setContentTitle("Clipboard received")
            .setContentText(preview)
            .setStyle(NotificationCompat.BigTextStyle().bigText(preview))
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .setTimeoutAfter(TIMEOUT_MS)
            .build()

        context.getSystemService(NotificationManager::class.java).notify(id, notification)
    }

    private fun showImageNotification(id: Int, imageData: ByteArray, copyIntent: Intent) {
        val bitmap = BitmapFactory.decodeByteArray(imageData, 0, imageData.size)

        val pendingIntent = PendingIntent.getBroadcast(
            context, id, copyIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val builder = NotificationCompat.Builder(context, CHANNEL_ID)
            .setContentTitle("Image received")
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .setTimeoutAfter(TIMEOUT_MS)

        if (bitmap != null) {
            builder.setStyle(
                NotificationCompat.BigPictureStyle().bigPicture(bitmap)
            )
        } else {
            builder.setContentText("Image (could not generate preview)")
        }

        context.getSystemService(NotificationManager::class.java).notify(id, builder.build())
    }

    private fun writeImageToCache(data: ByteArray, id: Int): android.net.Uri {
        val dir = File(context.cacheDir, "clip_images").apply { mkdirs() }
        val file = File(dir, "clip_$id.png")
        file.writeBytes(data)
        return FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
    }

    private fun createChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Received Content",
            NotificationManager.IMPORTANCE_DEFAULT,
        )
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    companion object {
        const val CHANNEL_ID = "fling_content"
        private const val FIRST_NOTIFICATION_ID = 100
        private const val TIMEOUT_MS = 5L * 60 * 1000
        private const val ACTION_COPY = "dev.davidfdev.fling.COPY_CLIP"
        private const val EXTRA_NOTIFICATION_ID = "notification_id"
        private const val EXTRA_TYPE = "clip_type"
        private const val EXTRA_TEXT = "clip_text"
        private const val EXTRA_URI = "clip_uri"
    }
}
