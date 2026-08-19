package dev.davidfdev.fling.content

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.core.app.NotificationCompat
import dev.davidfdev.fling.ClipActionActivity
import dev.davidfdev.fling.R
import dev.davidfdev.fling.data.ClipImageCache
import dev.davidfdev.fling.data.ClipItem
import java.util.concurrent.atomic.AtomicInteger

class NotificationContentNotifier(private val context: Context) : ContentNotifier {

    private val nextId = AtomicInteger(FIRST_NOTIFICATION_ID)

    init {
        createChannel()
    }

    override fun notify(item: ClipItem) {
        val notificationId = nextId.getAndIncrement()

        val textExtra = if (item.type.startsWith("text/")) String(item.data) else null
        val uriExtra = if (item.type == "image/png") {
            ClipImageCache.write(context, item).toString()
        } else {
            null
        }

        val tapPending = actionIntent(ClipActionActivity.ACTION_TAP_COPY, item, notificationId, textExtra, uriExtra)
        val copyPending = actionIntent(ClipActionActivity.ACTION_COPY, item, notificationId, textExtra, uriExtra)
        val sharePending = actionIntent(ClipActionActivity.ACTION_SHARE, item, notificationId, textExtra, uriExtra)

        when {
            item.type.startsWith("text/") -> {
                showTextNotification(notificationId, textExtra!!, tapPending, copyPending, sharePending)
            }
            item.type == "image/png" -> {
                showImageNotification(notificationId, item.data, tapPending, copyPending, sharePending)
            }
        }
    }

    /// Builds the launcher for one notification action.
    ///
    /// A PendingIntent is identified by its request code plus the action, component and
    /// data of its intent — never by its extras. Tagging each intent with the clip's own
    /// data URI keeps every clip's actions distinct, so the system cannot hand a
    /// notification the payload belonging to a different clip.
    private fun actionIntent(
        action: String,
        item: ClipItem,
        notificationId: Int,
        text: String?,
        uri: String?,
    ): PendingIntent {
        val intent = Intent(context, ClipActionActivity::class.java).apply {
            this.action = action
            data = Uri.parse("$CLIP_SCHEME://clip/${item.id}")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            putExtra(ClipActionActivity.EXTRA_NOTIFICATION_ID, notificationId)
            putExtra(ClipActionActivity.EXTRA_TYPE, item.type)
            text?.let { putExtra(ClipActionActivity.EXTRA_TEXT, it) }
            uri?.let { putExtra(ClipActionActivity.EXTRA_URI, it) }
        }
        return PendingIntent.getActivity(
            context, 0, intent,
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
        private const val CLIP_SCHEME = "fling"
    }
}
