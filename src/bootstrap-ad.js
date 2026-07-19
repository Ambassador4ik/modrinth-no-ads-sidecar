(function () {
	'use strict'

	var stateKey = '__modrinthNoAdsSidecarAdV1'

	function errorText(error) {
		try {
			return String((error && error.message) || error || 'unknown error').slice(0, 240)
		} catch (_) {
			return 'unknown error'
		}
	}

	function isVerifiedAdView(internals) {
		var metadata = internals && internals.metadata
		var webview = metadata && metadata.currentWebview
		var windowMetadata = metadata && metadata.currentWindow
		var wrapperUrl =
			location.hostname === 'modrinth.com' &&
			/^\/wrapper\/app-ads(?:-cookie)?\/?$/.test(location.pathname)
		var transientWindowsView =
			location.href === 'about:blank' &&
			webview &&
			webview.label === 'ads-window' &&
			windowMetadata &&
			windowMetadata.label === 'main'

		return (
			(wrapperUrl && (!webview || webview.label === 'ads-window')) ||
			transientWindowsView
		)
	}

	function status(state) {
		return {
			kind: 'ad',
			verified: state.verified,
			hideOk: state.hideOk,
			wrapperInstalled: false,
			styleInstalled: true,
			active: state.verified && state.hideOk,
			error: state.lastError,
		}
	}

	function install() {
		if (window !== window.top) {
			return Promise.resolve({
				kind: 'ad',
				verified: false,
				hideOk: false,
				wrapperInstalled: false,
				styleInstalled: true,
				active: false,
				error: 'not-top-frame',
			})
		}

		var existing = window[stateKey]
		if (existing && typeof existing.tick === 'function') {
			return existing.tick()
		}

		var state = {
			timer: null,
			hideInFlight: null,
			verified: false,
			hideOk: false,
			lastError: null,
		}

		state.schedule = function () {
			window.clearTimeout(state.timer)
			state.timer = window.setTimeout(
				function () {
					Promise.resolve(state.tick()).then(state.schedule, state.schedule)
				},
				state.verified ? 250 : 50,
			)
		}

		state.tick = function () {
			var internals = window.__TAURI_INTERNALS__
			state.verified = isVerifiedAdView(internals)
			if (!state.verified) {
				state.hideOk = false
				state.lastError = 'ad-view-not-ready'
				return Promise.resolve(status(state))
			}

			// Prevent a one-frame flash while the native hide command is in flight.
			if (document.documentElement) {
				document.documentElement.style.setProperty('visibility', 'hidden', 'important')
				document.documentElement.style.setProperty('pointer-events', 'none', 'important')
			}

			var invoke =
				(window.__TAURI__ &&
					window.__TAURI__.core &&
					typeof window.__TAURI__.core.invoke === 'function' &&
					window.__TAURI__.core.invoke) ||
				(internals && typeof internals.invoke === 'function' && internals.invoke.bind(internals))

			if (typeof invoke !== 'function') {
				state.hideOk = false
				state.lastError = 'native-invoke-not-ready'
				return Promise.resolve(status(state))
			}

			if (state.hideInFlight) {
				return state.hideInFlight.then(function () {
					return status(state)
				})
			}

			try {
				state.hideInFlight = Promise.resolve(
					invoke('plugin:ads|hide_ads_window', { reset: true }),
				).then(
					function () {
						state.hideOk = true
						state.lastError = null
					},
					function (error) {
						state.hideOk = false
						state.lastError = 'native-hide: ' + errorText(error)
					},
				)
			} catch (error) {
				state.hideOk = false
				state.lastError = 'native-hide: ' + errorText(error)
				return Promise.resolve(status(state))
			}

			return state.hideInFlight.then(function () {
				state.hideInFlight = null
				return status(state)
			})
		}

		window[stateKey] = state
		return state.tick().then(function (result) {
			state.schedule()
			return result
		})
	}

	return install()
})()
