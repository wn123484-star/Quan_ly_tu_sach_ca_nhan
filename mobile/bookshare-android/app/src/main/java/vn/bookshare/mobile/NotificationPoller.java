package vn.bookshare.mobile;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;
import android.webkit.CookieManager;

import androidx.annotation.NonNull;
import androidx.core.app.ActivityCompat;
import androidx.core.app.NotificationCompat;
import androidx.core.app.NotificationManagerCompat;
import androidx.work.Constraints;
import androidx.work.ExistingPeriodicWorkPolicy;
import androidx.work.ExistingWorkPolicy;
import androidx.work.NetworkType;
import androidx.work.OneTimeWorkRequest;
import androidx.work.PeriodicWorkRequest;
import androidx.work.WorkManager;
import androidx.work.Worker;
import androidx.work.WorkerParameters;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.TimeUnit;

public class NotificationPoller extends Worker {

    public static final String CHANNEL_ID = "bookshare_alerts";
    private static final String PERIODIC_WORK_NAME = "bookshare_notif_periodic";
    private static final String ONETIME_WORK_NAME = "bookshare_notif_now";
    private static final String PREFS_NAME = "bookshare_mobile_prefs";
    private static final String KEY_LAST_NOTIF_ID = "last_notification_id";

    public NotificationPoller(@NonNull Context context, @NonNull WorkerParameters params) {
        super(context, params);
    }

    public static void ensureChannel(@NonNull Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.notif_channel_name),
                    NotificationManager.IMPORTANCE_HIGH
            );
            channel.setDescription(context.getString(R.string.notif_channel_desc));
            channel.setLockscreenVisibility(Notification.VISIBILITY_PUBLIC);
            NotificationManager manager = context.getSystemService(NotificationManager.class);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    public static void schedulePeriodic(@NonNull Context context) {
        ensureChannel(context);
        Constraints constraints = new Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build();

        PeriodicWorkRequest request = new PeriodicWorkRequest.Builder(NotificationPoller.class, 15, TimeUnit.MINUTES)
                .setConstraints(constraints)
                .build();

        WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                PERIODIC_WORK_NAME,
                ExistingPeriodicWorkPolicy.KEEP,
                request
        );
    }

    public static void triggerNow(@NonNull Context context) {
        ensureChannel(context);
        Constraints constraints = new Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build();

        OneTimeWorkRequest request = new OneTimeWorkRequest.Builder(NotificationPoller.class)
                .setConstraints(constraints)
                .build();

        WorkManager.getInstance(context).enqueueUniqueWork(
                ONETIME_WORK_NAME,
                ExistingWorkPolicy.REPLACE,
                request
        );
    }

    public static void showNativeNotification(
            @NonNull Context context,
            int notificationId,
            @NonNull String title,
            @NonNull String message,
            String targetUrl) {
        ensureChannel(context);

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
                ActivityCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            return;
        }

        Intent intent = new Intent(context, MainActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        if (targetUrl != null && !targetUrl.trim().isEmpty()) {
            intent.putExtra(MainActivity.EXTRA_TARGET_URL, targetUrl.trim());
        }

        int pendingFlags = PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE;
        PendingIntent pendingIntent = PendingIntent.getActivity(context, notificationId, intent, pendingFlags);

        NotificationCompat.Builder builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                .setSmallIcon(R.drawable.ic_launcher_foreground)
                .setContentTitle(title)
                .setContentText(message)
                .setStyle(new NotificationCompat.BigTextStyle().bigText(message))
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
                .setAutoCancel(true)
                .setContentIntent(pendingIntent);

        NotificationManagerCompat.from(context).notify(notificationId, builder.build());
    }

    @NonNull
    @Override
    public Result doWork() {
        Context context = getApplicationContext();
        ensureChannel(context);

        String cookies = CookieManager.getInstance().getCookie(BuildConfig.BASE_URL);
        if (cookies == null || cookies.trim().isEmpty()) {
            return Result.success();
        }

        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        int lastId = prefs.getInt(KEY_LAST_NOTIF_ID, 0);

        HttpURLConnection conn = null;
        try {
            URL endpoint = new URL(BuildConfig.BASE_URL + "/api/mobile/notifications/unread?sinceId=" + lastId);
            conn = (HttpURLConnection) endpoint.openConnection();
            conn.setRequestMethod("GET");
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(10000);
            conn.setRequestProperty("Cookie", cookies);
            conn.setRequestProperty("Accept", "application/json");
            conn.setRequestProperty("User-Agent", "BookShareAndroidApp/1.0");

            int status = conn.getResponseCode();
            if (status != HttpURLConnection.HTTP_OK) {
                return Result.success();
            }

            StringBuilder body = new StringBuilder();
            try (BufferedReader reader = new BufferedReader(
                    new InputStreamReader(conn.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    body.append(line);
                }
            }

            JSONObject json = new JSONObject(body.toString());
            JSONArray items = json.optJSONArray("notifications");
            if (items == null || items.length() == 0) {
                return Result.success();
            }

            int maxId = lastId;
            for (int i = 0; i < items.length(); i++) {
                JSONObject item = items.optJSONObject(i);
                if (item == null) {
                    continue;
                }
                int id = item.optInt("id", 0);
                String title = item.optString("title", "Thông báo BookShare");
                String message = item.optString("message", "");
                String targetUrl = item.optString("targetUrl", "/S18NotificationHub");

                if (id > maxId) {
                    maxId = id;
                }
                if (id > 0 && !message.isEmpty()) {
                    showNativeNotification(context, id, title, message, targetUrl);
                }
            }

            if (maxId > lastId) {
                prefs.edit().putInt(KEY_LAST_NOTIF_ID, maxId).apply();
            }
        } catch (Exception ignored) {
            // Mất kết nối tạm thời: giữ yên, WorkManager chạy lại chu kỳ kế tiếp
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }

        return Result.success();
    }
}
