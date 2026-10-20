import { useEffect, useRef } from 'react';

// Prerendered content stays visible by default. Only supported clients prepare
// offscreen targets, so an already-visible block never flashes back to opacity 0.
export function useLandingMotion() {
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const root = rootRef.current;
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    if (!root || preference.matches || typeof IntersectionObserver === 'undefined' ||
        typeof Element.prototype.animate !== 'function') return;

    const active = new Map<HTMLElement, Animation>();
    let stopped = false;
    const settings = new Map<HTMLElement, { duration: number; distance: number; delay: number }>();
    const register = (selector: string, duration: number, distance: number, stagger = 0, delay = 0) => {
      root.querySelectorAll<HTMLElement>(selector).forEach((element, index) => {
        // Leave the first viewport and restored-scroll content exactly as painted.
        if (element.getBoundingClientRect().top < window.innerHeight) return;
        settings.set(element, { duration, distance, delay: delay + index * stagger });
      });
    };
    register('.landing-dashboard', 650, 12);
    register('.landing-section-heading, .product-story .story-copy', 600, 12);
    register('.product-story .story-art', 600, 12, 0, 80);
    register('.landing-security, .cta-content', 550, 10);
    register('.setup-steps > li', 550, 10, 60);

    const reveal = (element: HTMLElement) => {
      element.removeAttribute('data-landing-pending');
      element.style.removeProperty('--landing-reveal-distance');
    };

    const observer = new IntersectionObserver(entries => {
      if (stopped) return;
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        const element = entry.target as HTMLElement;
        observer.unobserve(element);
        if (!element.hasAttribute('data-landing-pending')) continue;
        // A queued observer callback can still arrive after Reduce Motion changes.
        if (preference.matches || element.contains(document.activeElement)) {
          reveal(element);
          continue;
        }
        const { duration, distance, delay } = settings.get(element)!;
        const animation = element.animate([
          { opacity: 0, transform: `translateY(${distance}px)` },
          { opacity: 1, transform: 'none' },
        ], { duration, delay, easing: 'cubic-bezier(0.22, 0.61, 0.36, 1)', fill: 'both' });
        active.set(element, animation);
        animation.onfinish = () => {
          reveal(element);
          active.delete(element);
          animation.cancel();
        };
      }
    }, { threshold: 0.12, rootMargin: '0px 0px -32px 0px' });

    const stop = () => {
      stopped = true;
      observer.disconnect();
      settings.forEach((_, element) => reveal(element));
      active.forEach(animation => animation.cancel());
      active.clear();
    };
    const onPreferenceChange = () => { if (preference.matches) stop(); };
    // Keyboard navigation never has to wait for a link's container to fade in.
    const onFocus = (event: FocusEvent) => {
      if (!(event.target instanceof Node)) return;
      for (const element of settings.keys()) {
        if (element.contains(event.target)) {
          observer.unobserve(element);
          reveal(element);
          active.get(element)?.cancel();
          active.delete(element);
        }
      }
    };
    settings.forEach(({ distance }, element) => {
      element.style.setProperty('--landing-reveal-distance', `${distance}px`);
      element.setAttribute('data-landing-pending', '');
      observer.observe(element);
    });
    preference.addEventListener('change', onPreferenceChange);
    root.addEventListener('focusin', onFocus);

    return () => {
      stop();
      preference.removeEventListener('change', onPreferenceChange);
      root.removeEventListener('focusin', onFocus);
    };
  }, []);

  return rootRef;
}
