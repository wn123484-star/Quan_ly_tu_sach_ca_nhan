package vn.bookshare.mobile;

import android.Manifest;
import android.annotation.SuppressLint;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.provider.Settings;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.SslErrorHandler;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.activity.OnBackPressedCallback;
import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.browser.customtabs.CustomTabsIntent;
import androidx.core.content.ContextCompat;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import org.json.JSONObject;

import java.util.Locale;

public class MainActivity extends AppCompatActivity {

    public static final String EXTRA_TARGET_URL = "extra_target_url";
    private static final String OFFLINE_ASSET_PREFIX = "file:///android_asset/";
    private static final String OFFLINE_ASSET_URL = "file:///android_asset/offline.html";
    private static final long SLOW_LOAD_HINT_MS = 7000L;
    private static final long LOAD_TIMEOUT_MS = 20000L;

    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private WebView webView;
    private SwipeRefreshLayout swipeRefresh;
    private ProgressBar progressBar;
    private LinearLayout loadingOverlay;
    private TextView loadingStatusText;
    private Button btnRetryLoad;

    private PermissionRequest pendingWebPermissionRequest;
    private ValueCallback<Uri[]> pendingFilePathCallback;

    private String lastAttemptedUrl = BuildConfig.BASE_URL;
    private boolean mainFrameErrorOccurred = false;
    private boolean pullToRefreshEnabled = true;
    private boolean notificationPromptRequested = false;

    private final Runnable slowLoadHintRunnable = () -> {
        if (loadingOverlay != null && loadingOverlay.getVisibility() == View.VISIBLE) {
            if (loadingStatusText != null) {
                loadingStatusText.setText(R.string.loading_slow_hint);
            }
            if (btnRetryLoad != null) {
                btnRetryLoad.setVisibility(View.VISIBLE);
            }
        }
    };

    private final Runnable progressBarCleanupRunnable = () -> {
        if (progressBar != null) {
            progressBar.setVisibility(View.GONE);
        }
        if (swipeRefresh != null) {
            swipeRefresh.setRefreshing(false);
        }
    };

    private final Runnable loadTimeoutRunnable = () -> {
        if (webView == null || isFinishing() || isDestroyed()) {
            return;
        }
        String current = webView.getUrl();
        if (current != null && current.startsWith(OFFLINE_ASSET_PREFIX)) {
            return;
        }
        mainFrameErrorOccurred = true;
        webView.stopLoading();
        hideLoadingUi();
        showOfflineFallback("timeout");
    };

    // Chỉ yêu cầu quyền CAMERA khi người dùng bấm chức năng quét QR / mã vạch cần mở máy ảnh
    private final ActivityResultLauncher<String> cameraPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), granted -> {
                PermissionRequest request = pendingWebPermissionRequest;
                pendingWebPermissionRequest = null;
                if (request == null) {
                    return;
                }
                try {
                    if (granted) {
                        request.grant(new String[]{ PermissionRequest.RESOURCE_VIDEO_CAPTURE });
                    } else {
                        request.deny();
                        Toast.makeText(
                                MainActivity.this,
                                R.string.camera_permission_denied_hint,
                                Toast.LENGTH_LONG
                        ).show();
                    }
                } catch (IllegalStateException ignored) {
                    // Yêu cầu camera đã bị đóng trước khi người dùng chọn quyền
                }
            });

    private final ActivityResultLauncher<String> notificationPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), ignored -> { });

    private final ActivityResultLauncher<Intent> fileChooserLauncher =
            registerForActivityResult(new ActivityResultContracts.StartActivityForResult(), result -> {
                if (pendingFilePathCallback == null) {
                    return;
                }
                Uri[] results = WebChromeClient.FileChooserParams.parseResult(
                        result.getResultCode(),
                        result.getData()
                );
                pendingFilePathCallback.onReceiveValue(results);
                pendingFilePathCallback = null;
            });

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        swipeRefresh = findViewById(R.id.swipeRefresh);
        webView = findViewById(R.id.bookShareWebView);
        progressBar = findViewById(R.id.webProgressBar);
        loadingOverlay = findViewById(R.id.loadingOverlay);
        loadingStatusText = findViewById(R.id.loadingStatusText);
        btnRetryLoad = findViewById(R.id.btnRetryLoad);

        swipeRefresh.setColorSchemeResources(R.color.bookshare_primary);
        swipeRefresh.setOnChildScrollUpCallback((parent, child) ->
                !pullToRefreshEnabled || (webView != null && webView.getScrollY() > 0));
        swipeRefresh.setOnRefreshListener(this::reloadCurrentOrBaseUrl);

        if (btnRetryLoad != null) {
            btnRetryLoad.setOnClickListener(v -> loadTargetUrl(lastAttemptedUrl, true));
        }

        configureWebView();
        setupBackNavigation();
        NotificationPoller.schedulePeriodic(this);

        String initialUrl = resolveUrlFromIntent(getIntent());
        loadTargetUrl(initialUrl, true);
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String targetUrl = resolveUrlFromIntent(intent);
        if (webView != null && targetUrl != null && !targetUrl.isEmpty()) {
            loadTargetUrl(targetUrl, false);
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        if (webView != null) {
            webView.onPause();
        }
        CookieManager.getInstance().flush();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (webView != null) {
            webView.onResume();
        }
        NotificationPoller.triggerNow(this);
    }

    @Override
    protected void onDestroy() {
        cancelLoadWatchdog();
        if (pendingWebPermissionRequest != null) {
            try {
                pendingWebPermissionRequest.deny();
            } catch (IllegalStateException ignored) {
                // Bỏ qua nếu yêu cầu đã hủy
            }
            pendingWebPermissionRequest = null;
        }
        if (pendingFilePathCallback != null) {
            pendingFilePathCallback.onReceiveValue(null);
            pendingFilePathCallback = null;
        }
        if (webView != null) {
            webView.stopLoading();
            webView.removeJavascriptInterface("BookShareAndroidBridge");
            webView.setWebChromeClient(null);
            webView.setWebViewClient(new WebViewClient());
            if (webView.getParent() instanceof ViewGroup) {
                ((ViewGroup) webView.getParent()).removeView(webView);
            }
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    private void configureWebView() {
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setUseWideViewPort(true);
        settings.setLoadWithOverviewMode(true);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);
        settings.setSupportMultipleWindows(false);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);
        settings.setUserAgentString(settings.getUserAgentString() + " BookShareAndroidApp/1.0");

        CookieManager cookieManager = CookieManager.getInstance();
        cookieManager.setAcceptCookie(true);
        cookieManager.setAcceptThirdPartyCookies(webView, true);

        webView.addJavascriptInterface(new BookShareAndroidBridge(), "BookShareAndroidBridge");

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                if (uri == null) {
                    return false;
                }
                if (!request.isForMainFrame()) {
                    return false;
                }
                String url = uri.toString();

                if ("bookshare".equalsIgnoreCase(uri.getScheme()) && "open".equalsIgnoreCase(uri.getHost())) {
                    String deepPath = sanitizeRelativePath(uri.getQueryParameter("path"));
                    if (deepPath != null) {
                        loadTargetUrl(BuildConfig.BASE_URL + deepPath, false);
                    }
                    return true;
                }

                if (shouldOpenExternalOAuth(uri)) {
                    openCustomTab(url);
                    return true;
                }

                if (isSameOrigin(uri)) {
                    return false;
                }

                try {
                    Intent external = new Intent(Intent.ACTION_VIEW, uri);
                    startActivity(external);
                } catch (ActivityNotFoundException ignored) {
                    // Không có ứng dụng phù hợp trên thiết bị
                }
                return true;
            }

            @Override
            public void onPageStarted(WebView view, String url, Bitmap favicon) {
                if (isHttpUrl(url)) {
                    lastAttemptedUrl = url;
                    mainFrameErrorOccurred = false;
                    startLoadWatchdog();
                    if (progressBar != null) {
                        progressBar.setVisibility(View.VISIBLE);
                        progressBar.setProgress(15);
                    }
                }
            }

            @Override
            public void onPageCommitVisible(WebView view, String url) {
                // Trang thật đã nhận và vẽ pixel đầu tiên: hủy watchdog timeout để tài nguyên phụ chậm không bao giờ đá ra offline.html
                if (!mainFrameErrorOccurred) {
                    cancelLoadWatchdog();
                    hideLoadingOverlay();
                    mainHandler.postDelayed(progressBarCleanupRunnable, 3500L);
                }
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                cancelLoadWatchdog();
                hideLoadingUi();
                CookieManager.getInstance().flush();

                if (isHttpUrl(url) && !mainFrameErrorOccurred) {
                    requestNotificationPermissionOnceAfterLoad();
                    NotificationPoller.triggerNow(MainActivity.this);
                }
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (!request.isForMainFrame()) {
                    return;
                }
                String failedUrl = request.getUrl() != null ? request.getUrl().toString() : "";
                if (!isHttpUrl(failedUrl)) {
                    return;
                }
                CharSequence description = error != null ? error.getDescription() : null;
                if (description != null && description.toString().contains("ERR_ABORTED")) {
                    return;
                }

                lastAttemptedUrl = failedUrl;
                mainFrameErrorOccurred = true;
                cancelLoadWatchdog();
                hideLoadingUi();
                showOfflineFallback("network");
            }

            @Override
            public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
                // Không bao giờ bỏ qua lỗi SSL để bảo đảm an toàn phiên đăng nhập
                handler.cancel();
                mainFrameErrorOccurred = true;
                cancelLoadWatchdog();
                hideLoadingUi();
                showOfflineFallback("ssl");
            }

            @Override
            public boolean onRenderProcessGone(WebView view, RenderProcessGoneDetail detail) {
                if (!isFinishing() && !isDestroyed()) {
                    recreate();
                }
                return true;
            }
        });

        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onProgressChanged(WebView view, int newProgress) {
                if (progressBar != null) {
                    if (newProgress < 100) {
                        progressBar.setVisibility(View.VISIBLE);
                        progressBar.setProgress(Math.max(newProgress, 10));
                    } else {
                        progressBar.setVisibility(View.GONE);
                    }
                }
                if (newProgress >= 75 && !mainFrameErrorOccurred) {
                    hideLoadingOverlay();
                }
                if (newProgress >= 100) {
                    cancelLoadWatchdog();
                    if (swipeRefresh != null) {
                        swipeRefresh.setRefreshing(false);
                    }
                }
            }

            @Override
            public void onPermissionRequest(PermissionRequest request) {
                runOnUiThread(() -> {
                    if (request == null || !isSameOrigin(request.getOrigin())) {
                        if (request != null) {
                            request.deny();
                        }
                        return;
                    }
                    boolean asksCamera = false;
                    for (String res : request.getResources()) {
                        if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(res)) {
                            asksCamera = true;
                            break;
                        }
                    }
                    if (!asksCamera) {
                        request.deny();
                        return;
                    }

                    if (ContextCompat.checkSelfPermission(MainActivity.this, Manifest.permission.CAMERA)
                            == PackageManager.PERMISSION_GRANTED) {
                        try {
                            request.grant(new String[]{ PermissionRequest.RESOURCE_VIDEO_CAPTURE });
                        } catch (IllegalStateException ignored) {
                            // Bỏ qua nếu yêu cầu không còn hiệu lực
                        }
                    } else {
                        if (pendingWebPermissionRequest != null) {
                            try {
                                pendingWebPermissionRequest.deny();
                            } catch (IllegalStateException ignored) {
                                // Bỏ qua nếu yêu cầu cũ đã đóng
                            }
                        }
                        pendingWebPermissionRequest = request;
                        cameraPermissionLauncher.launch(Manifest.permission.CAMERA);
                    }
                });
            }

            @Override
            public void onPermissionRequestCanceled(PermissionRequest request) {
                runOnUiThread(() -> {
                    if (pendingWebPermissionRequest == request) {
                        pendingWebPermissionRequest = null;
                    }
                });
            }

            @Override
            public boolean onShowFileChooser(
                    WebView webView,
                    ValueCallback<Uri[]> filePathCallback,
                    FileChooserParams fileChooserParams) {
                if (pendingFilePathCallback != null) {
                    pendingFilePathCallback.onReceiveValue(null);
                }
                pendingFilePathCallback = filePathCallback;

                try {
                    Intent chooser = buildSafeFileChooserIntent(fileChooserParams);
                    fileChooserLauncher.launch(Intent.createChooser(chooser, "Chọn ảnh hoặc tệp"));
                    return true;
                } catch (ActivityNotFoundException e) {
                    pendingFilePathCallback = null;
                    return false;
                }
            }
        });
    }

    private Intent buildSafeFileChooserIntent(WebChromeClient.FileChooserParams params) {
        if (params != null) {
            String[] acceptTypes = params.getAcceptTypes();
            if (isImageOnlyAcceptTypes(acceptTypes)) {
                Intent imageIntent = new Intent(Intent.ACTION_GET_CONTENT);
                imageIntent.addCategory(Intent.CATEGORY_OPENABLE);
                imageIntent.setType("image/*");
                if (params.getMode() == WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE) {
                    imageIntent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                }
                return imageIntent;
            }
            return params.createIntent();
        }
        Intent fallback = new Intent(Intent.ACTION_GET_CONTENT);
        fallback.addCategory(Intent.CATEGORY_OPENABLE);
        fallback.setType("*/*");
        return fallback;
    }

    private boolean isImageOnlyAcceptTypes(String[] acceptTypes) {
        if (acceptTypes == null || acceptTypes.length == 0) {
            return false;
        }
        boolean hasValue = false;
        for (String raw : acceptTypes) {
            if (raw == null || raw.trim().isEmpty()) {
                continue;
            }
            hasValue = true;
            String item = raw.trim().toLowerCase(Locale.ROOT);
            boolean isImg = item.startsWith("image/")
                    || item.equals(".jpg")
                    || item.equals(".jpeg")
                    || item.equals(".png")
                    || item.equals(".webp")
                    || item.equals(".gif")
                    || item.equals(".svg");
            if (!isImg) {
                return false;
            }
        }
        return hasValue;
    }

    private void loadTargetUrl(String url, boolean showOverlay) {
        if (webView == null) {
            return;
        }
        String safeUrl = isHttpUrl(url) ? url : BuildConfig.BASE_URL;
        lastAttemptedUrl = safeUrl;
        mainFrameErrorOccurred = false;
        if (showOverlay) {
            showLoadingOverlay();
        }
        startLoadWatchdog();
        webView.loadUrl(safeUrl);
    }

    private void reloadCurrentOrBaseUrl() {
        if (webView == null) {
            return;
        }
        String current = webView.getUrl();
        if (current == null || current.trim().isEmpty()
                || "about:blank".equalsIgnoreCase(current)
                || current.startsWith(OFFLINE_ASSET_PREFIX)) {
            loadTargetUrl(lastAttemptedUrl, true);
        } else {
            mainFrameErrorOccurred = false;
            startLoadWatchdog();
            webView.reload();
        }
    }

    private void showOfflineFallback(String reason) {
        if (webView == null) {
            return;
        }
        String target = (reason != null && !reason.isEmpty())
                ? OFFLINE_ASSET_URL + "?reason=" + Uri.encode(reason)
                : OFFLINE_ASSET_URL;
        webView.loadUrl(target);
    }

    private void showLoadingOverlay() {
        if (loadingStatusText != null) {
            loadingStatusText.setText(R.string.loading_connecting);
        }
        if (btnRetryLoad != null) {
            btnRetryLoad.setVisibility(View.GONE);
        }
        if (loadingOverlay != null) {
            loadingOverlay.setVisibility(View.VISIBLE);
        }
    }

    private void hideLoadingOverlay() {
        if (loadingOverlay != null && loadingOverlay.getVisibility() != View.GONE) {
            loadingOverlay.setVisibility(View.GONE);
        }
    }

    private void hideLoadingUi() {
        hideLoadingOverlay();
        if (swipeRefresh != null) {
            swipeRefresh.setRefreshing(false);
        }
        if (progressBar != null) {
            progressBar.setVisibility(View.GONE);
        }
    }

    private void startLoadWatchdog() {
        cancelLoadWatchdog();
        mainHandler.postDelayed(slowLoadHintRunnable, SLOW_LOAD_HINT_MS);
        mainHandler.postDelayed(loadTimeoutRunnable, LOAD_TIMEOUT_MS);
    }

    private void cancelLoadWatchdog() {
        mainHandler.removeCallbacks(slowLoadHintRunnable);
        mainHandler.removeCallbacks(loadTimeoutRunnable);
        mainHandler.removeCallbacks(progressBarCleanupRunnable);
    }

    private void setupBackNavigation() {
        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                if (webView != null && webView.canGoBack()) {
                    String current = webView.getUrl();
                    if (current != null && current.startsWith(OFFLINE_ASSET_PREFIX)) {
                        loadTargetUrl(BuildConfig.BASE_URL, true);
                        return;
                    }
                    webView.goBack();
                } else {
                    setEnabled(false);
                    getOnBackPressedDispatcher().onBackPressed();
                }
            }
        });
    }

    private void requestNotificationPermissionOnceAfterLoad() {
        if (notificationPromptRequested) {
            return;
        }
        notificationPromptRequested = true;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
                ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS);
        }
    }

    private boolean isHttpUrl(String url) {
        return url != null && (url.startsWith("https://") || url.startsWith("http://"));
    }

    private String sanitizeRelativePath(String path) {
        if (path == null) {
            return null;
        }
        String trimmed = path.trim();
        if (trimmed.startsWith("/") && !trimmed.startsWith("//")) {
            return trimmed;
        }
        return null;
    }

    private boolean isSameOrigin(Uri uri) {
        if (uri == null) {
            return false;
        }
        Uri baseUri = Uri.parse(BuildConfig.BASE_URL);
        String host = uri.getHost();
        String baseHost = baseUri.getHost();
        return host != null && baseHost != null && host.equalsIgnoreCase(baseHost);
    }

    private boolean shouldOpenExternalOAuth(Uri uri) {
        if (uri == null) {
            return false;
        }
        String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(Locale.ROOT);
        String path = uri.getPath() == null ? "" : uri.getPath().toLowerCase(Locale.ROOT);
        if (isSameOrigin(uri) && path.contains("/s01authotpgoogle/completemobileoauth")) {
            return false;
        }
        return host.equals("accounts.google.com")
                || host.endsWith(".accounts.google.com")
                || (isSameOrigin(uri) && (path.contains("/s01authotpgoogle/googlelogin") || path.contains("/signin-google")));
    }

    private void openCustomTab(String url) {
        if (!isHttpUrl(url)) {
            return;
        }
        try {
            CustomTabsIntent customTabsIntent = new CustomTabsIntent.Builder().build();
            customTabsIntent.launchUrl(this, Uri.parse(url));
        } catch (ActivityNotFoundException ignored) {
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
            } catch (ActivityNotFoundException ignoredAgain) {
                // Bỏ qua nếu thiết bị không có trình duyệt
            }
        }
    }

    private void openSystemAppSettings() {
        try {
            Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            intent.setData(Uri.fromParts("package", getPackageName(), null));
            startActivity(intent);
        } catch (ActivityNotFoundException ignored) {
            // Bỏ qua nếu không mở được trang Cài đặt hệ thống
        }
    }

    @NonNull
    private String resolveUrlFromIntent(Intent intent) {
        if (intent != null) {
            String extra = intent.getStringExtra(EXTRA_TARGET_URL);
            if (extra != null && !extra.trim().isEmpty()) {
                String trimmed = extra.trim();
                String safeRel = sanitizeRelativePath(trimmed);
                if (safeRel != null) {
                    return BuildConfig.BASE_URL + safeRel;
                }
                Uri parsedExtra = Uri.parse(trimmed);
                if (isSameOrigin(parsedExtra)) {
                    return trimmed;
                }
            }

            Uri data = intent.getData();
            if (data != null && "bookshare".equalsIgnoreCase(data.getScheme()) && "open".equalsIgnoreCase(data.getHost())) {
                String safeRel = sanitizeRelativePath(data.getQueryParameter("path"));
                if (safeRel != null) {
                    return BuildConfig.BASE_URL + safeRel;
                }
            }
        }
        return BuildConfig.BASE_URL;
    }

    public class BookShareAndroidBridge {
        @JavascriptInterface
        public boolean isNativeApp() {
            return true;
        }

        @JavascriptInterface
        public String getAppInfo() {
            try {
                JSONObject info = new JSONObject();
                info.put("platform", "android");
                info.put("version", BuildConfig.VERSION_NAME);
                info.put("baseUrl", BuildConfig.BASE_URL);
                return info.toString();
            } catch (Exception e) {
                return "{\"platform\":\"android\",\"version\":\"4.0.0\"}";
            }
        }

        @JavascriptInterface
        public void vibrateShort() {
            try {
                Vibrator vibrator = getSystemService(Vibrator.class);
                if (vibrator != null && vibrator.hasVibrator()) {
                    vibrator.vibrate(VibrationEffect.createOneShot(45, VibrationEffect.DEFAULT_AMPLITUDE));
                }
            } catch (Exception ignored) {
                // Bỏ qua nếu thiết bị không hỗ trợ rung
            }
        }

        @JavascriptInterface
        public void openExternalAuth(String url) {
            runOnUiThread(() -> openCustomTab(url));
        }

        @JavascriptInterface
        public void openAppSettings() {
            runOnUiThread(MainActivity.this::openSystemAppSettings);
        }

        @JavascriptInterface
        public void setPullToRefreshEnabled(boolean enabled) {
            runOnUiThread(() -> {
                pullToRefreshEnabled = enabled;
                if (swipeRefresh != null) {
                    swipeRefresh.setEnabled(enabled);
                    if (!enabled) {
                        swipeRefresh.setRefreshing(false);
                    }
                }
            });
        }

        @JavascriptInterface
        public void syncNotificationsNow() {
            NotificationPoller.triggerNow(MainActivity.this);
        }

        @JavascriptInterface
        public void pushLocalNotification(String title, String message, String targetUrl) {
            NotificationPoller.showNativeNotification(
                    MainActivity.this,
                    (int) (System.currentTimeMillis() & 0x7fffffff),
                    title != null ? title : "Thông báo BookShare",
                    message != null ? message : "",
                    targetUrl
            );
        }

        @JavascriptInterface
        public void retryLastPage() {
            runOnUiThread(() -> loadTargetUrl(lastAttemptedUrl, true));
        }

        @JavascriptInterface
        public void reloadHome() {
            runOnUiThread(() -> loadTargetUrl(BuildConfig.BASE_URL, true));
        }
    }
}
