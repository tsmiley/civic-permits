// The skip link is a plain in-page anchor, which works without any script. With Blazor's enhanced
// navigation active, activating it scrolls to #main but leaves keyboard focus on the link, so the next
// Tab press goes back to the top of the page. This moves focus to the target as well.
document.addEventListener('click', (event) => {
  const link = event.target instanceof Element ? event.target.closest('a.skip-link') : null;
  if (!link) {
    return;
  }

  const target = document.getElementById(link.getAttribute('href').slice(1));
  if (!target) {
    return;
  }

  event.preventDefault();
  target.focus();
  target.scrollIntoView();
});
