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
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.TimeUnit;

public class NotificationPoller extends Worker {

    public static final String CHANNEL_ID = "bookshare_alerts";
    private static final String PERIODIC_WORK_NAME = "bookshare_notif_periodic";
    private static final String ONETIME_WORK_NAME = "bookshare_notif_now";
    private static final String PREFS_NAME = "bookshare_mobile_prefs";
    private static final String KEY_LAST_NOTIF_ID = "last_notification_id";
    private static final String KEY_DELIVERED_IDS = "delivered_notification_ids";
    private static final int MAX_TRACKED_DELIVERED = 120;

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

    public static void clearDeliveredState(@NonNull Context context) {
        try {
            NotificationManagerCompat.from(context).cancelAll();
        } catch (Exception ignored) {
            // Bỏ qua nếu hệ thống từ chối hủy thông báo
        }
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        prefs.edit().clear().apply();
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

        String resolvedTarget = appendNotifIdToTargetUrl(targetUrl, notificationId);
        Intent intent = new Intent(context, MainActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        if (resolvedTarget != null && !resolvedTarget.isEmpty()) {
            intent.putExtra(MainActivity.EXTRA_TARGET_URL, resolvedTarget);
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

    private static String appendNotifIdToTargetUrl(String targetUrl, int notificationId) {
        String clean = targetUrl == null ? "" : targetUrl.trim();
        if (clean.isEmpty()) {
            return notificationId > 0
                    ? "/S18NotificationHub?highlight=" + notificationId + "&notifId=" + notificationId
                    : "/S18NotificationHub";
        }
        if (notificationId <= 0 || clean.contains("notifId=")) {
            return clean;
        }
        return clean + (clean.contains("?") ? "&" : "?") + "notifId=" + notificationId;
    }

    @NonNull
    @Override
    public Result doWork() {
        Context context = getApplicationContext();
        ensureChannel(context);

        String cookies = CookieManager.getInstance().getCookie(BuildConfig.BASE_URL);
        if (cookies == null || cookies.trim().isEmpty()) {
            cookies = CookieManager.getInstance().getCookie(BuildConfig.BASE_URL + "/");
        }
        if (cookies == null || cookies.trim().isEmpty()) {
            return Result.success();
        }

        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        int lastId = prefs.getInt(KEY_LAST_NOTIF_ID, 0);
        Set<String> storedDelivered = prefs.getStringSet(KEY_DELIVERED_IDS, Collections.emptySet());
        Set<String> deliveredIds = new HashSet<>(storedDelivered != null ? storedDelivered : Collections.emptySet());

        HttpURLConnection conn = null;
        try {
            URL endpoint = new URL(BuildConfig.BASE_URL + "/api/mobile/notifications/unread?sinceId=" + lastId);
            conn = (HttpURLConnection) endpoint.openConnection();
            conn.setRequestMethod("GET");
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(10000);
            conn.setRequestProperty("Cookie", cookies);
            conn.setRequestProperty("Accept", "application/json");
            conn.setRequestProperty("User-Agent", "BookShareAndroidApp/" + BuildConfig.VERSION_NAME);

            int status = conn.getResponseCode();
            if (status == HttpURLConnection.HTTP_UNAUTHORIZED || status != HttpURLConnection.HTTP_OK) {
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
            int latestNotificationId = json.optInt("latestNotificationId", 0);
            JSONArray items = json.optJSONArray("notifications");

            // Chỉ hạ mốc lastId khi máy chủ xác nhận ID lớn nhất thực tế nhỏ hơn mốc cũ (do reset CSDL)
            if (latestNotificationId > 0 && latestNotificationId < lastId) {
                prefs.edit()
                        .putInt(KEY_LAST_NOTIF_ID, latestNotificationId)
                        .putStringSet(KEY_DELIVERED_IDS, Collections.emptySet())
                        .apply();
                return Result.success();
            }

            if (items == null || items.length() == 0) {
                return Result.success();
            }

            int maxId = Math.max(lastId, latestNotificationId);
            boolean deliveredChanged = false;
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
                String idStr = String.valueOf(id);
                if (id > 0 && !message.isEmpty() && !deliveredIds.contains(idStr)) {
                    showNativeNotification(context, id, title, message, targetUrl);
                    deliveredIds.add(idStr);
                    deliveredChanged = true;
                }
            }

            if (maxId > lastId || deliveredChanged) {
                SharedPreferences.Editor editor = prefs.edit().putInt(KEY_LAST_NOTIF_ID, maxId);
                if (deliveredChanged) {
                    editor.putStringSet(KEY_DELIVERED_IDS, trimDeliveredIds(deliveredIds));
                }
                editor.apply();
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

    private static Set<String> trimDeliveredIds(Set<String> source) {
        if (source.size() <= MAX_TRACKED_DELIVERED) {
            return new HashSet<>(source);
        }
        List<Integer> nums = new ArrayList<>();
        for (String s : source) {
            try {
                nums.add(Integer.parseInt(s));
            } catch (NumberFormatException ignored) {
                // Bỏ qua giá trị không hợp lệ
            }
        }
        Collections.sort(nums);
        int start = Math.max(0, nums.size() - MAX_TRACKED_DELIVERED);
        Set<String> trimmed = new HashSet<>();
        for (int i = start; i < nums.size(); i++) {
            trimmed.add(String.valueOf(nums.get(i)));
        }
        return trimmed;
    }
}
