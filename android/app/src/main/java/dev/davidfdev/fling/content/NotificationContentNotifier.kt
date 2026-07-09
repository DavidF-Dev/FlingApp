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
            val type = intent.getStringExtra(EXTRA_TYPE) ?: return
            val notificationId = intent.getIntExtra(EXTRA_NOTIFICATION_ID, -1)

            when (intent.action) {
                ACTION_TAP_COPY -> {
                    copyToClipboard(ctx, type, intent)
                    Toast.makeText(ctx, "Copied to clipboard", Toast.LENGTH_SHORT).show()
                    if (notificationId != -1) {
                        ctx.getSystemService(NotificationManager::class.java).cancel(notificationId)
                    }
                }
                ACTION_COPY -> {
                    copyToClipboard(ctx, type, intent)
                    Toast.makeText(ctx, "Copied to clipboard", Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    init {
        val filter = IntentFilter().apply {
            addAction(ACTION_TAP_COPY)
            addAction(ACTION_COPY)
        }
        ContextCompat.registerReceiver(context, receiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
        createChannel()
    }

    override fun notify(item: ClipItem) {
        val notificationId = nextId.getAndIncrement()

        val baseExtras = mapOf(
            EXTRA_NOTIFICATION_ID to notificationId,
            EXTRA_TYPE to item.type,
        )

        val textExtra = if (item.type.startsWith("text/")) String(item.data) else null
        val uriExtra = if (item.type == "image/png") {
            writeImageToCache(item.data, notificationId).toString()
        } else {
            null
        }

        val tapIntent = buildBroadcastIntent(ACTION_TAP_COPY, baseExtras, textExtra, uriExtra)
        val copyIntent = buildBroadcastIntent(ACTION_COPY, baseExtras, textExtra, uriExtra)

        val tapPending = PendingIntent.getBroadcast(
            context, notificationId * 10, tapIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val copyPending = PendingIntent.getBroadcast(
            context, notificationId * 10 + 1, copyIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val sharePending = buildSharePendingIntent(notificationId, item, uriExtra)

        when {
            item.type.startsWith("text/") -> {
                showTextNotification(notificationId, textExtra!!, tapPending, copyPending, sharePending)
            }
            item.type == "image/png" -> {
                showImageNotification(notificationId, item.data, tapPending, copyPending, sharePending)
            }
        }
    }

    fun destroy() {
        context.unregisterReceiver(receiver)
    }

    private fun buildBroadcastIntent(
        action: String,
        extras: Map<String, Any>,
        text: String?,
        uri: String?,
    ): Intent = Intent(action).apply {
        setPackage(context.packageName)
        extras.forEach { (key, value) ->
            when (value) {
                is Int -> putExtra(key, value)
                is String -> putExtra(key, value)
            }
        }
        text?.let { putExtra(EXTRA_TEXT, it) }
        uri?.let { putExtra(EXTRA_URI, it) }
    }

    private fun copyToClipboard(ctx: Context, type: String, intent: Intent) {
        val clipboardManager = ctx.getSystemService(android.content.ClipboardManager::class.java)
        when {
            type.startsWith("text/") -> {
                val text = intent.getStringExtra(EXTRA_TEXT) ?: return
                clipboardManager.setPrimaryClip(android.content.ClipData.newPlainText("Fling", text))
            }
            type == "image/png" -> {
                val uriString = intent.getStringExtra(EXTRA_URI) ?: return
                val uri = android.net.Uri.parse(uriString)
                clipboardManager.setPrimaryClip(
                    android.content.ClipData.newUri(ctx.contentResolver, "Fling", uri),
                )
            }
        }
    }

    private fun buildSharePendingIntent(
        notificationId: Int,
        item: ClipItem,
        uriExtra: String?,
    ): PendingIntent {
        val shareIntent = when {
            item.type.startsWith("text/") -> Intent(Intent.ACTION_SEND).apply {
                type = "text/plain"
                putExtra(Intent.EXTRA_TEXT, String(item.data))
            }
            item.type == "image/png" -> Intent(Intent.ACTION_SEND).apply {
                type = "image/png"
                putExtra(Intent.EXTRA_STREAM, android.net.Uri.parse(uriExtra))
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            else -> Intent()
        }
        val chooser = Intent.createChooser(shareIntent, null).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        return PendingIntent.getActivity(
            context, notificationId * 10 + 2, chooser,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun showTextNotification(
        id: Int,
        text: String,
        tapPending: PendingIntent,
        copyPending: PendingIntent,
        sharePending: PendingIntent,
    ) {
        val preview = if (text.length > 100) text.take(100) + "…" else text

        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setContentTitle("Clipboard received")
            .setContentText(preview)
            .setStyle(NotificationCompat.BigTextStyle().bigText(preview))
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(tapPending)
            .addAction(0, "Copy", copyPending)
            .addAction(0, "Share", sharePending)
            .setTimeoutAfter(TIMEOUT_MS)
            .build()

        context.getSystemService(NotificationManager::class.java).notify(id, notification)
    }

    private fun showImageNotification(
        id: Int,
        imageData: ByteArray,
        tapPending: PendingIntent,
        copyPending: PendingIntent,
        sharePending: PendingIntent,
    ) {
        val bitmap = BitmapFactory.decodeByteArray(imageData, 0, imageData.size)

        val builder = NotificationCompat.Builder(context, CHANNEL_ID)
            .setContentTitle("Image received")
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(tapPending)
            .addAction(0, "Copy", copyPending)
            .addAction(0, "Share", sharePending)
            .setTimeoutAfter(TIMEOUT_MS)

        if (bitmap != null) {
            builder.setStyle(NotificationCompat.BigPictureStyle().bigPicture(bitmap))
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
        private const val ACTION_TAP_COPY = "dev.davidfdev.fling.TAP_COPY_CLIP"
        private const val ACTION_COPY = "dev.davidfdev.fling.COPY_CLIP"
        private const val EXTRA_NOTIFICATION_ID = "notification_id"
        private const val EXTRA_TYPE = "clip_type"
        private const val EXTRA_TEXT = "clip_text"
        private const val EXTRA_URI = "clip_uri"
    }
}
