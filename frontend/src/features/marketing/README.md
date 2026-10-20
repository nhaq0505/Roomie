# Landing motion

`LandingContent` keeps the existing layout and prerendered content. `useLandingMotion` adds one-time viewport reveals through IntersectionObserver and the Web Animations API; no animation library is needed.

- The first viewport, hero title, header and primary action render immediately and are never hidden/replayed after hydration. Offscreen dashboard: 650ms/12px. Feature copy/art: 600ms/12px, art delayed 80ms. Security/CTA/setup steps: 550ms/10px, steps staggered by 60ms.
- Opacity/transform are animated without changing dimensions or document flow. Finished animations are removed, returning to the original CSS appearance.
- Each target is unobserved after its first entrance. Supported clients prepare offscreen targets at opacity 0 before they enter the viewport, preventing a visible-to-hidden flash. Without JavaScript or either API the base/prerendered HTML remains visible. Cleanup and Reduce Motion clear all pending states.
- Reduce Motion skips reveals and smooth anchor scrolling. Turning it on while the page is open cancels active animations and stops remaining reveals for that mount.
- Keyboard focus cancels animation on the focused link's container. Unmount disconnects observation and removes listeners/active animations.
- Smooth anchor scrolling is scoped to an HTML document containing `.landing-page`, with the existing section scroll margin. Other app routes retain their scroll behavior.

Validation: `npm run build`, `npm run check:architecture`, `npm run i18n:check`, `npm run test:e2e`. `e2e/landing-motion.spec.ts` covers one-time reveals, stable dimensions, reduced motion (initial/live), prerender without JavaScript, focus during animation and unsupported API fallback.

Local visual evidence lives under ignored `artifacts/landing-motion-2026-10-19`: full-page before/after PNGs and text/geometry metrics at 1440px and 375px. Compare after all reveals finish; animations intentionally differ during playback. External font requests were blocked equally in both captures.
