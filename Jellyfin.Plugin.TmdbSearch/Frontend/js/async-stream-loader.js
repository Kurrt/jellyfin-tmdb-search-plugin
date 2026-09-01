/**
 * Remux-style async MediaSources loader for jellyfin-web item details.
 *
 * Patches ApiClient.getItem so the details page can render metadata immediately
 * (Fields=ChildCount) while Gelato resolves addon streams. A spinner appears in
 * the Version/Audio/Subtitles panel instead of blocking the whole page.
 */
(function () {
    var STYLE_ID = 'tmdbsearch-async-streams-css';
    var PENDING_CLASS = 'tmdbsearch-streams-pending';
    var READY_CLASS = 'tmdbsearch-streams-ready';
    var SPINNER_CLASS = 'tmdbsearch-sources-loading';
    var EMPTY_CLASS = 'tmdbsearch-no-streams';
    var PATCH_FLAG = '_tmdbsearchGetItemPatched';
    var ORIGINAL_FLAG = '_tmdbsearchOriginalGetItem';
    var videoNavCount = 0;

    /**
     * Injects play-button and spinner CSS once per document.
     */
    function injectCss() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }

        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = [
            '@keyframes tmdbsearch-spin { to { transform: rotate(360deg); } }',
            '.' + PENDING_CLASS + ' .btnPlay {',
            '  opacity: 0.4;',
            '  pointer-events: none;',
            '  cursor: default;',
            '}',
            '.' + READY_CLASS + ' .btnPlay {',
            '  opacity: 1;',
            '  pointer-events: auto;',
            '  cursor: pointer;',
            '}'
        ].join('\n');

        var host = document.head || document.documentElement;
        if (host) {
            host.appendChild(style);
        }
    }

    /**
     * Escapes text for HTML option labels.
     *
     * @param {*} value Raw display value.
     * @returns {string} Escaped HTML.
     */
    function escHtml(value) {
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    /**
     * Reads a provider id with case-insensitive key matching.
     *
     * @param {object} item Jellyfin item DTO.
     * @param {string} key Provider name such as Stremio.
     * @returns {string} Provider id or empty string.
     */
    function providerId(item, key) {
        var ids = item && item.ProviderIds;
        if (!ids) {
            return '';
        }

        if (ids[key]) {
            return String(ids[key]);
        }

        var lower = key.toLowerCase();
        for (var name in ids) {
            if (Object.prototype.hasOwnProperty.call(ids, name) && name.toLowerCase() === lower) {
                return String(ids[name]);
            }
        }

        return '';
    }

    /**
     * Returns true when the item is a Gelato/TMDB stub that still needs stream sync.
     *
     * @param {object} item Jellyfin item DTO.
     * @returns {boolean} True when async MediaSources loading should run.
     */
    function needsAsyncStreams(item) {
        if (!item) {
            return false;
        }

        if (providerId(item, 'Stremio')) {
            return true;
        }

        var locationType = item.LocationType;
        if (locationType === 'Virtual' || locationType === 3) {
            return true;
        }

        var sources = item.MediaSources;
        if (!sources || !sources.length) {
            return false;
        }

        for (var i = 0; i < sources.length; i++) {
            if (sources[i] && sources[i].Path === '/stub') {
                return true;
            }
        }

        return false;
    }

    /**
     * Finds the visible details-page content root.
     *
     * @returns {Element|null} Visible `.detailPagePrimaryContent`, if any.
     */
    function getDetailsPage() {
        var all = document.querySelectorAll('.detailPagePrimaryContainer');
        for (var i = 0; i < all.length; i++) {
            if (all[i].offsetParent !== null) {
                return all[i].querySelector('.detailPagePrimaryContent');
            }
        }

        return null;
    }

    /**
     * Finds the visible primary container (play button lives here).
     *
     * @returns {Element|null} Visible `.detailPagePrimaryContainer`, if any.
     */
    function getVisiblePrimaryContainer() {
        var all = document.querySelectorAll('.detailPagePrimaryContainer');
        for (var i = 0; i < all.length; i++) {
            if (all[i].offsetParent !== null) {
                return all[i];
            }
        }

        return null;
    }

    /**
     * True when the current URL belongs to this item's detail page.
     *
     * @param {string} itemId Item GUID, with or without dashes.
     * @returns {boolean} True when the item id appears in the location.
     */
    function isCurrentPage(itemId) {
        var href = location.href;
        var noDash = String(itemId).replace(/-/g, '');
        return href.indexOf(itemId) !== -1 || href.indexOf(noDash) !== -1;
    }

    /**
     * Hides the stub-rendered track selects while streams load.
     *
     * @param {Element} page Details content root.
     */
    function hideTrackControls(page) {
        var form = page.querySelector('.trackSelections');
        if (!form) {
            return;
        }

        var containers = form.querySelectorAll(
            '.selectSourceContainer, .selectVideoContainer, .selectAudioContainer, .selectSubtitlesContainer');
        for (var i = 0; i < containers.length; i++) {
            containers[i].classList.add('hide');
        }
    }

    /**
     * Removes spinner and empty-state nodes from the track panel.
     *
     * @param {Element} page Details content root.
     */
    function removeSpinner(page) {
        var spinner = page.querySelector('.' + SPINNER_CLASS);
        if (spinner && spinner.parentNode) {
            spinner.parentNode.removeChild(spinner);
        }

        var empty = page.querySelector('.' + EMPTY_CLASS);
        if (empty && empty.parentNode) {
            empty.parentNode.removeChild(empty);
        }

        var form = page.querySelector('.trackSelections');
        if (form && !form._tmdbsearchLoaded) {
            form.classList.add('hide');
        }
    }

    /**
     * Shows a small spinner inside `.trackSelections`.
     *
     * @param {Element} page Details content root.
     */
    function showSpinner(page) {
        removeSpinner(page);
        var form = page.querySelector('.trackSelections');
        if (!form) {
            return;
        }

        hideTrackControls(page);
        var spin = document.createElement('div');
        spin.className = SPINNER_CLASS;
        spin.setAttribute('role', 'status');
        spin.setAttribute('aria-label', 'Loading streams');
        spin.style.cssText = [
            'width:1.4em',
            'height:1.4em',
            'border:2px solid rgba(255,255,255,0.2)',
            'border-top-color:rgba(255,255,255,0.8)',
            'border-radius:50%',
            'animation:tmdbsearch-spin 0.7s linear infinite',
            'margin:0.4em auto',
            'display:block',
            'flex-shrink:0'
        ].join(';');
        form.insertBefore(spin, form.firstChild);
        form.classList.remove('hide');
    }

    /**
     * Shows a non-intrusive empty state when no streams were found.
     *
     * @param {Element} page Details content root.
     */
    function showNoStreams(page) {
        removeSpinner(page);
        var form = page.querySelector('.trackSelections');
        if (!form) {
            return;
        }

        hideTrackControls(page);
        var msg = document.createElement('div');
        msg.className = EMPTY_CLASS;
        msg.style.cssText = 'color:rgba(255,255,255,0.5);font-size:0.85em;text-align:center;padding:0.4em 0;';
        msg.textContent = 'No streams available';
        form.insertBefore(msg, form.firstChild);
        form.classList.remove('hide');
    }

    /**
     * Marks the visible play button as waiting on streams.
     */
    function markPlayPending() {
        var container = getVisiblePrimaryContainer();
        if (!container) {
            return;
        }

        container.classList.add(PENDING_CLASS);
        container.classList.remove(READY_CLASS);
    }

    /**
     * Enables the visible play button.
     */
    function markPlayReady() {
        var container = getVisiblePrimaryContainer();
        if (!container) {
            return;
        }

        container.classList.remove(PENDING_CLASS);
        container.classList.add(READY_CLASS);
    }

    /**
     * Enables play on the visible container, including late-rendered views.
     *
     * @param {string} itemId Item whose page must still be current.
     */
    function watchAndEnable(itemId) {
        var seen = typeof WeakSet === 'function' ? new WeakSet() : null;

        function tryEnable() {
            if (!isCurrentPage(itemId)) {
                observer.disconnect();
                return;
            }

            var container = getVisiblePrimaryContainer();
            if (container && (!seen || !seen.has(container))) {
                if (seen) {
                    seen.add(container);
                }

                container.classList.remove(PENDING_CLASS);
                container.classList.add(READY_CLASS);
            }
        }

        var observer = new MutationObserver(function () {
            tryEnable();
        });
        observer.observe(document.body, { childList: true, subtree: true });
        tryEnable();
        setTimeout(function () {
            observer.disconnect();
        }, 5000);
    }

    /**
     * Fills video/audio/subtitle selects for one media source.
     *
     * @param {Element} page Details content root.
     * @param {Array} mediaSources MediaSources from the follow-up getItem.
     * @param {string} selectedSourceId Selected source id.
     */
    function renderTracksForSource(page, mediaSources, selectedSourceId) {
        var form = page.querySelector('.trackSelections');
        if (form) {
            form._tmdbsearchRendering = true;
        }

        var source = null;
        for (var i = 0; i < mediaSources.length; i++) {
            if (mediaSources[i].Id === selectedSourceId) {
                source = mediaSources[i];
                break;
            }
        }

        if (!source) {
            source = mediaSources[0];
        }

        var streams = source.MediaStreams || [];
        var videoTracks = streams.filter(function (stream) {
            return stream.Type === 'Video';
        });
        var selVideo = page.querySelector('.selectVideo');
        if (selVideo) {
            if (selVideo.setLabel) {
                selVideo.setLabel('Video');
            }

            selVideo.innerHTML = videoTracks.map(function (track) {
                return '<option value="' + track.Index + '">'
                    + escHtml(track.DisplayTitle || track.Codec || String(track.Index))
                    + '</option>';
            }).join('');
            selVideo.setAttribute('disabled', 'disabled');
            var videoBox = page.querySelector('.selectVideoContainer');
            if (videoBox) {
                videoBox.classList[videoTracks.length ? 'remove' : 'add']('hide');
            }
        }

        var audioTracks = streams.filter(function (stream) {
            return stream.Type === 'Audio';
        });
        var selAudio = page.querySelector('.selectAudio');
        if (selAudio) {
            if (selAudio.setLabel) {
                selAudio.setLabel('Audio');
            }

            var defAudio = source.DefaultAudioStreamIndex;
            selAudio.innerHTML = audioTracks.map(function (track) {
                var selected = track.Index === defAudio ? ' selected' : '';
                return '<option value="' + track.Index + '"' + selected + '>'
                    + escHtml(track.DisplayTitle || String(track.Index))
                    + '</option>';
            }).join('');
            if (audioTracks.length > 1) {
                selAudio.removeAttribute('disabled');
            } else {
                selAudio.setAttribute('disabled', 'disabled');
            }

            var audioBox = page.querySelector('.selectAudioContainer');
            if (audioBox) {
                audioBox.classList[audioTracks.length ? 'remove' : 'add']('hide');
            }
        }

        var subTracks = streams.filter(function (stream) {
            return stream.Type === 'Subtitle';
        });
        var selSubs = page.querySelector('.selectSubtitles');
        if (selSubs) {
            if (selSubs.setLabel) {
                selSubs.setLabel('Subtitles');
            }

            var defSub = source.DefaultSubtitleStreamIndex == null ? -1 : source.DefaultSubtitleStreamIndex;
            var offSelected = defSub === -1 ? ' selected' : '';
            selSubs.innerHTML = '<option value="-1"' + offSelected + '>Off</option>'
                + subTracks.map(function (track) {
                    var selected = track.Index === defSub ? ' selected' : '';
                    return '<option value="' + track.Index + '"' + selected + '>'
                        + escHtml(track.DisplayTitle || String(track.Index))
                        + '</option>';
                }).join('');
            if (subTracks.length) {
                selSubs.removeAttribute('disabled');
            } else {
                selSubs.setAttribute('disabled', 'disabled');
            }

            var subBox = page.querySelector('.selectSubtitlesContainer');
            if (subBox) {
                subBox.classList[subTracks.length ? 'remove' : 'add']('hide');
            }
        }

        if (form) {
            setTimeout(function () {
                form._tmdbsearchRendering = false;
            }, 0);
        }
    }

    /**
     * Re-applies loaded sources if jellyfin-web wipes the track panel.
     *
     * @param {Element} page Details content root.
     */
    function attachTrackSelectionsGuard(page) {
        var form = page.querySelector('.trackSelections');
        if (!form || form._tmdbsearchObsAttached) {
            return;
        }

        form._tmdbsearchObsAttached = true;
        var observer = new MutationObserver(function () {
            if (form._tmdbsearchRendering || !form._tmdbsearchLoaded) {
                return;
            }

            var sources = window._tmdbsearchCurrentMediaSources;
            if (!sources || !sources.length) {
                return;
            }

            renderAsyncTrackSelections(page, sources);
        });
        observer.observe(form, { childList: true, subtree: true });
    }

    /**
     * Renders the Version select and nested track dropdowns.
     *
     * @param {Element} page Details content root.
     * @param {Array} mediaSources MediaSources from the follow-up getItem.
     */
    function renderAsyncTrackSelections(page, mediaSources) {
        var form = page.querySelector('.trackSelections');
        if (!form) {
            return;
        }

        form._tmdbsearchRendering = true;
        var selSrc = page.querySelector('.selectSource');
        var selectedId = mediaSources[0].Id;
        if (selSrc) {
            selSrc.innerHTML = mediaSources.map(function (source) {
                var selected = source.Id === selectedId ? ' selected' : '';
                return '<option value="' + escHtml(source.Id) + '"' + selected + '>'
                    + escHtml(source.Name || source.Id)
                    + '</option>';
            }).join('');
            if (selSrc.setLabel) {
                selSrc.setLabel('Version');
            }

            var sourceBox = page.querySelector('.selectSourceContainer');
            if (sourceBox) {
                sourceBox.classList[mediaSources.length > 1 ? 'remove' : 'add']('hide');
            }
        }

        renderTracksForSource(page, mediaSources, selectedId);
        window._tmdbsearchCurrentMediaSources = mediaSources;
        form._tmdbsearchMediaSources = mediaSources;
        form._tmdbsearchLoaded = true;

        var source = mediaSources[0];
        var streams = source.MediaStreams || [];
        var hasChoice = mediaSources.length > 1
            || streams.filter(function (stream) {
                return stream.Type === 'Audio';
            }).length > 1
            || streams.some(function (stream) {
                return stream.Type === 'Subtitle';
            });
        if (hasChoice) {
            form.classList.remove('hide');
        } else {
            form.classList.add('hide');
        }

        setTimeout(function () {
            form._tmdbsearchRendering = false;
        }, 0);
    }

    /**
     * Re-renders tracks when the user picks a different version.
     *
     * @param {Element} page Details content root.
     */
    function attachSourceChangeHandler(page) {
        var sel = page.querySelector('.selectSource');
        if (!sel || sel._tmdbsearchHandlerAttached) {
            return;
        }

        sel._tmdbsearchHandlerAttached = true;
        sel.addEventListener('change', function () {
            var sources = window._tmdbsearchCurrentMediaSources;
            if (!sources) {
                return;
            }

            renderTracksForSource(page, sources, sel.value);
        });
    }

    /**
     * Appends a Fields query parameter to an item URL.
     *
     * @param {string} baseUrl Item URL.
     * @param {string} fields Field list, e.g. ChildCount,ProviderIds,Path.
     * @returns {string} URL with Fields query.
     */
    function withFields(baseUrl, fields) {
        var separator = baseUrl.indexOf('?') >= 0 ? '&' : '?';
        return baseUrl + separator + 'Fields=' + fields;
    }

    /**
     * Loads MediaSources for a Gelato-backed movie/episode and fills the panel.
     *
     * @param {object} apiClient Jellyfin ApiClient.
     * @param {string} baseUrl Item URL.
     * @param {string} itemId Item GUID.
     */
    function loadStreamsAsync(apiClient, baseUrl, itemId) {
        var capturedNav = ++videoNavCount;
        var sourcesUrl = withFields(baseUrl, 'MediaSources');
        var sourcesFetch = apiClient.getJSON(sourcesUrl);

        markPlayPending();
        setTimeout(function () {
            if (!isCurrentPage(itemId)) {
                return;
            }

            var page = getDetailsPage();
            if (!page) {
                return;
            }

            var form = page.querySelector('.trackSelections');
            if (form && form._tmdbsearchNavCount === capturedNav) {
                return;
            }

            showSpinner(page);
            markPlayPending();
        }, 0);

        sourcesFetch.then(function (full) {
            if (!isCurrentPage(itemId)) {
                return;
            }

            var sources = full && full.MediaSources;
            var streamsReady = !!(sources && sources.length && full.LocationType !== 'Virtual');

            if (streamsReady) {
                watchAndEnable(itemId);
            }

            (function apply() {
                if (!isCurrentPage(itemId)) {
                    return;
                }

                var page = getDetailsPage();
                if (!page) {
                    setTimeout(apply, 50);
                    return;
                }

                var form = page.querySelector('.trackSelections');
                if (form && form._tmdbsearchNavCount === capturedNav) {
                    return;
                }

                removeSpinner(page);
                if (streamsReady) {
                    renderAsyncTrackSelections(page, sources);
                    attachSourceChangeHandler(page);
                    attachTrackSelectionsGuard(page);
                    var loaded = page.querySelector('.trackSelections');
                    if (loaded) {
                        loaded._tmdbsearchNavCount = capturedNav;
                    }
                } else {
                    showNoStreams(page);
                }
            }());
        }).catch(function () {
            if (!isCurrentPage(itemId)) {
                return;
            }

            var page = getDetailsPage();
            if (!page) {
                return;
            }

            var form = page.querySelector('.trackSelections');
            if (!form || !form._tmdbsearchLoaded) {
                removeSpinner(page);
                showNoStreams(page);
            }
        });
    }

    /**
     * Wraps ApiClient.getItem so details pages skip MediaSources on first paint.
     *
     * @param {object} apiClient window.ApiClient instance.
     */
    function patchApiClientProto(apiClient) {
        var proto = Object.getPrototypeOf(apiClient);
        if (!proto || proto[PATCH_FLAG]) {
            return;
        }

        proto[PATCH_FLAG] = true;
        proto[ORIGINAL_FLAG] = proto.getItem;

        proto.getItem = function (userId, itemId) {
            var self = this;
            var original = proto[ORIGINAL_FLAG];
            if (typeof itemId !== 'string') {
                return original.call(self, userId, itemId);
            }

            if (!isCurrentPage(itemId)) {
                return original.call(self, userId, itemId);
            }

            var baseUrl = self.getUrl('Users/' + userId + '/Items/' + itemId);
            var fastUrl = withFields(baseUrl, 'ChildCount,ProviderIds,Path');

            return self.getJSON(fastUrl).then(function (item) {
                if (!isCurrentPage(itemId)) {
                    return item;
                }

                var type = item && item.Type;
                var isMovieOrEpisode = type === 'Movie' || type === 'Episode';
                if (!isMovieOrEpisode) {
                    watchAndEnable(itemId);
                    return item;
                }

                if (!needsAsyncStreams(item)) {
                    watchAndEnable(itemId);
                    self.getJSON(withFields(baseUrl, 'MediaSources')).then(function (full) {
                        if (!isCurrentPage(itemId)) {
                            return;
                        }

                        var sources = full && full.MediaSources;
                        if (!sources || !sources.length) {
                            return;
                        }

                        var page = getDetailsPage();
                        if (!page) {
                            return;
                        }

                        renderAsyncTrackSelections(page, sources);
                        attachSourceChangeHandler(page);
                        attachTrackSelectionsGuard(page);
                    });
                    return item;
                }

                loadStreamsAsync(self, baseUrl, itemId);
                return item;
            });
        };
    }

    injectCss();

    var realApiClient = null;
    try {
        Object.defineProperty(window, 'ApiClient', {
            configurable: true,
            get: function () {
                return realApiClient;
            },
            set: function (value) {
                realApiClient = value;
                if (value) {
                    patchApiClientProto(value);
                }
            }
        });
    } catch (error) {
        (function poll() {
            if (window.ApiClient) {
                patchApiClientProto(window.ApiClient);
            } else {
                setTimeout(poll, 50);
            }
        }());
    }
}());
