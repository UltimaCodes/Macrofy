// Macrofy website: nav state, the animated macro keyboard, and scroll animations.
// Everything is visible without JavaScript; animations only add motion on top, and are
// skipped for people who've asked their system for reduced motion.

(() => {
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // ---- small things that don't need GSAP ----

  document.querySelectorAll(".js-year").forEach((el) => (el.textContent = new Date().getFullYear()));

  const nav = document.querySelector(".nav");
  const onScroll = () => nav.classList.toggle("is-scrolled", window.scrollY > 24);
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();

  // Highlight the nav link for the section in view.
  const links = [...document.querySelectorAll(".nav__links a")];
  const sections = links.map((a) => document.querySelector(a.getAttribute("href"))).filter(Boolean);
  if ("IntersectionObserver" in window && sections.length) {
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          links.forEach((a) => a.classList.toggle("is-active", a.getAttribute("href") === "#" + entry.target.id));
        });
      },
      { rootMargin: "-45% 0px -50% 0px" }
    );
    sections.forEach((s) => io.observe(s));
  }

  // FAQ: keep one answer open at a time.
  document.querySelectorAll(".faq details").forEach((d) => {
    d.addEventListener("toggle", () => {
      if (!d.open) return;
      document.querySelectorAll(".faq details[open]").forEach((other) => other !== d && (other.open = false));
    });
  });

  // ---- the hero keyboard ----

  // Each step presses a key and says what happened, like the app does.
  const demo = [
    { key: "q", icon: "i-rec", text: "<strong>OBS</strong> · Recording started" },
    { key: "1", icon: "i-grid", text: "<strong>OBS</strong> · Switched to Scene 1" },
    { key: "e", icon: "i-rocket", text: "Opened <strong>Spotify</strong>" },
    { key: "x", icon: "i-text", text: "Typed <strong>“Best regards, Ryaan”</strong>" },
    { key: "z", icon: "i-micoff", text: "<strong>Mic</strong> muted" },
    { key: "s", icon: "i-volume", text: "Volume <strong>up</strong>", repeat: 3 },
    { key: "t", icon: "i-globe", text: "Opened <strong>youtube.com</strong>" },
  ];

  const toast = document.querySelector(".toast");
  const toastIcon = document.querySelector(".js-toast-icon");
  const toastText = document.querySelector(".js-toast-text");
  const keyEl = (k) => document.querySelector(`.k[data-key="${k}"]`);

  function showStep(step) {
    toastIcon.setAttribute("href", "#" + step.icon);
    toastText.innerHTML = step.text;
  }

  // Without GSAP (blocked CDN) or with reduced motion: a still board with the first result.
  const hasGsap = () => typeof window.gsap !== "undefined";

  const revealPage = () => document.documentElement.classList.remove("js-anim");

  window.addEventListener("DOMContentLoaded", () => {
    if (reduceMotion || !hasGsap()) {
      showStep(demo[0]);
      revealPage();
      return;
    }

    const { gsap } = window;
    const plugins = [window.ScrollTrigger, window.SplitText].filter(Boolean);
    gsap.registerPlugin(...plugins);
    const hasST = typeof window.ScrollTrigger !== "undefined";
    const hasSplit = typeof window.SplitText !== "undefined";

    // Entrance
    const intro = gsap.timeline({ defaults: { ease: "power3.out" } });
    intro.from(".nav", { y: -30, opacity: 0, duration: 0.8 });
    if (hasSplit) {
      const split = new SplitText("[data-hero-title]", { type: "words", mask: "words" });
      intro.from(split.words, { yPercent: 110, duration: 0.9, stagger: 0.05 }, "-=0.45");
    } else {
      intro.from("[data-hero-title]", { y: 30, opacity: 0, duration: 0.9 }, "-=0.45");
    }
    intro
      .from("[data-hero]", { y: 22, opacity: 0, duration: 0.8, stagger: 0.08 }, "-=0.6")
      .from("[data-hero-board]", { y: 60, opacity: 0, scale: 0.96, duration: 1.1 }, "-=0.55")
      .from(".toast", { y: 14, opacity: 0, duration: 0.5 }, "-=0.3");
    // The tweens above have already set their starting (transparent) state, so un-hiding
    // now can't flash the finished layout.
    revealPage();

    // Key presses, forever
    const loop = gsap.timeline({ repeat: -1, delay: 1.9 });
    demo.forEach((step) => {
      const el = keyEl(step.key);
      const presses = step.repeat || 1;
      loop.to(toast, { y: 8, opacity: 0, duration: 0.18, ease: "power2.in" });
      loop.call(() => showStep(step));
      for (let i = 0; i < presses; i++) {
        loop.call(() => el.classList.add("is-pressed"));
        loop.to({}, { duration: presses > 1 ? 0.16 : 0.22 });
        loop.call(() => el.classList.remove("is-pressed"));
        if (presses > 1) loop.to({}, { duration: 0.1 });
      }
      loop.to(toast, { y: 0, opacity: 1, duration: 0.32, ease: "power3.out" }, "<-0.1");
      loop.to({}, { duration: 1.5 });
    });

    // Pause the loop when the hero is off screen.
    if (hasST) {
      window.ScrollTrigger.create({
        trigger: ".hero",
        start: "top bottom",
        end: "bottom top",
        onToggle: (self) => (self.isActive ? loop.play() : loop.pause()),
      });
    }

    if (!hasST) return;

    // Statement: words light up as you scroll past.
    const statement = document.querySelector("[data-reveal-words]");
    if (statement && hasSplit) {
      const words = new SplitText(statement, { type: "words" }).words;
      gsap.set(words, { opacity: 0.14 });
      gsap.to(words, {
        opacity: 1,
        ease: "none",
        stagger: 0.1,
        scrollTrigger: { trigger: statement, start: "top 78%", end: "bottom 40%", scrub: 0.6 },
      });
    }

    // Section headings and blocks fade up as they arrive.
    gsap.utils.toArray("[data-fade]").forEach((el) => {
      gsap.from(el, {
        y: 40,
        opacity: 0,
        duration: 0.9,
        ease: "power3.out",
        scrollTrigger: { trigger: el, start: "top 85%" },
      });
    });

    window.ScrollTrigger.batch("[data-stagger]", {
      start: "top 88%",
      onEnter: (batch) =>
        gsap.from(batch, { y: 36, opacity: 0, duration: 0.8, stagger: 0.08, ease: "power3.out", overwrite: true }),
    });

    // The app screenshot starts tilted back and settles flat as you scroll to it.
    const frame = document.querySelector("[data-tilt]");
    if (frame) {
      gsap.fromTo(
        frame,
        { rotateX: 22, y: 60, scale: 0.94 },
        {
          rotateX: 0,
          y: 0,
          scale: 1,
          ease: "none",
          scrollTrigger: { trigger: frame, start: "top 95%", end: "top 30%", scrub: 0.8 },
        }
      );
    }

    // Slow parallax on the background glow.
    gsap.to(".hero__glow", {
      yPercent: 18,
      ease: "none",
      scrollTrigger: { trigger: ".hero", start: "top top", end: "bottom top", scrub: true },
    });
  });
})();
