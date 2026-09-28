/**
 * inFaux Landing Page Script
 * FoxDen Software
 */

document.addEventListener('DOMContentLoaded', () => {
  // 1. One-Click Copy Buttons (Terminal & Mobile Notice)
  const copyButtons = document.querySelectorAll('.btn-copy');
  copyButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const targetId = btn.getAttribute('data-target');
      const targetEl = document.getElementById(targetId);
      if (!targetEl) return;

      const text = targetEl.textContent.trim();
      navigator.clipboard.writeText(text).then(() => {
        const originalContent = btn.innerHTML;
        btn.classList.add('copied');
        btn.innerHTML = `
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"></polyline></svg>
          <span>Copied!</span>
        `;
        setTimeout(() => {
          btn.classList.remove('copied');
          btn.innerHTML = originalContent;
        }, 2200);
      }).catch(err => {
        console.error('Failed to copy text: ', err);
      });
    });
  });

  // 2. Mobile Banner Detection & Dismiss
  const mobileNotice = document.getElementById('mobile-notice');
  const mobileNoticeClose = document.getElementById('mobile-notice-close');
  const isMobile = /Android|iPhone|iPad|iPod|Opera Mini|IEMobile|WPDesktop/i.test(navigator.userAgent) || window.innerWidth < 768;

  if (mobileNotice && isMobile && !sessionStorage.getItem('infaux-mobile-notice-dismissed')) {
    mobileNotice.style.display = 'block';
  }

  if (mobileNoticeClose) {
    mobileNoticeClose.addEventListener('click', () => {
      mobileNotice.style.display = 'none';
      sessionStorage.setItem('infaux-mobile-notice-dismissed', 'true');
    });
  }

  // 3. Screenshot Showcase Tab Switcher
  const showcaseTabs = document.querySelectorAll('.showcase-tab');
  const showcaseImg = document.getElementById('showcase-main-img');
  const showcaseCaption = document.getElementById('showcase-caption');

  const showcaseData = {
    dashboard: {
      img: 'assets/screenshots/inFaux-Main-Screen.png',
      caption: 'inFaux Native Dashboard — Real-time CPU, GPU, Memory, Network, and NVMe SMART thermals with zero kernel drivers.'
    },
    flux: {
      img: 'assets/screenshots/inFaux-Core-Temps.png',
      caption: 'Per-Core Silicon Flux (Per Core Temp Calculation) — Real-time Newtonian heat diffusion modeling across individual physical core pairs.'
    },
    streamdeck: {
      img: 'assets/screenshots/Stream-Deck.png',
      caption: 'Stream Deck Companion Suite — Hardware-accelerated 60fps canvas dials with multi-GPU tap-to-cycle.'
    }
  };

  showcaseTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      showcaseTabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');

      const target = tab.getAttribute('data-showcase');
      if (showcaseData[target] && showcaseImg) {
        showcaseImg.src = showcaseData[target].img;
        showcaseImg.alt = showcaseData[target].caption;
        if (showcaseCaption) showcaseCaption.textContent = showcaseData[target].caption;
      }
    });
  });

  // 4. Lightbox Modal
  const imgWrap = document.getElementById('showcase-img-wrap');
  const lightbox = document.getElementById('lightbox-modal');
  const lightboxImg = document.getElementById('lightbox-img');
  const lightboxClose = document.getElementById('lightbox-close');

  if (imgWrap && lightbox && lightboxImg) {
    imgWrap.addEventListener('click', () => {
      if (showcaseImg && showcaseImg.src) {
        lightboxImg.src = showcaseImg.src;
        lightbox.classList.add('active');
      }
    });

    if (lightboxClose) {
      lightboxClose.addEventListener('click', () => {
        lightbox.classList.remove('active');
      });
    }

    lightbox.addEventListener('click', (e) => {
      if (e.target === lightbox) {
        lightbox.classList.remove('active');
      }
    });
  }

  // 5. FAQ Accordion
  const faqItems = document.querySelectorAll('.faq-item');
  faqItems.forEach(item => {
    const question = item.querySelector('.faq-question');
    if (question) {
      question.addEventListener('click', () => {
        const isOpen = item.classList.contains('open');
        faqItems.forEach(i => i.classList.remove('open'));
        if (!isOpen) item.classList.add('open');
      });
    }
  });
});
