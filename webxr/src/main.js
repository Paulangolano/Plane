import './styles/main.css';
import { Game } from './core/game.js';

// Entry point: optional headset emulation, the start screen and the Enter VR button.

async function boot() {
  const params = new URLSearchParams(location.search);
  // ?emulate=quest2 installs Meta's IWER emulator, so the VR mode can be tested
  // in a desktop browser (used by the automated tests too).
  if (params.has('emulate')) {
    const { XRDevice, metaQuest2 } = await import('iwer');
    const device = new XRDevice(metaQuest2);
    device.installRuntime({ forceInstall: true });
    window.__xrDevice = device;
  }

  const game = new Game(document.getElementById('app'));
  window.__skyreach = game; // handy for debugging in the console and for tests

  const overlay = document.getElementById('overlay');
  const vrButton = document.getElementById('enter-vr');
  const desktopButton = document.getElementById('play-desktop');
  const vrNote = document.getElementById('vr-note');

  // Aircraft picker + assists toggle, remembered between visits.
  const radios = [...document.querySelectorAll('input[name="aircraft"]')];
  const assists = document.getElementById('assists');
  const helpText = document.getElementById('desktop-help-text');
  const HELP = {
    jet: 'W/S pitch · A/D roll · Shift/Ctrl throttle · Space boost · V camera · Enter restart',
    sr22: 'W/S A/D Q/E fly · Shift/Ctrl power · [ ] flaps · T/G trim · Z AP · L LVL · hold 9 chute · hold 0 Safe Return · V camera',
  };
  const showFor = (id) => {
    document.querySelectorAll('[data-for]').forEach((el) => el.classList.toggle('off', el.dataset.for !== id));
    helpText.textContent = HELP[id];
  };
  radios.forEach((r) => {
    r.checked = r.value === game.aircraft.id;
    r.addEventListener('change', () => {
      game.setAircraft(r.value);
      showFor(r.value);
    });
  });
  assists.checked = game.settings.assists !== false;
  assists.addEventListener('change', () => {
    game.settings.assists = assists.checked;
    if (game.aircraft.id === 'sr22') game.aircraft.model.assists = assists.checked;
    game.context().saveSettings();
  });
  showFor(game.aircraft.id);

  let supported = false;
  try {
    supported = !!navigator.xr && (await navigator.xr.isSessionSupported('immersive-vr'));
  } catch {
    supported = false;
  }

  if (supported) {
    vrButton.disabled = false;
    vrNote.textContent = 'Headset detected. Put it on and press Enter VR.';
  } else if (!window.isSecureContext) {
    vrNote.textContent = 'VR needs an https:// address. Open the game from its https link on your Quest.';
  } else {
    vrNote.textContent = 'No VR headset found. Open this page in the Meta Quest Browser to fly in VR.';
  }

  let session = null;
  vrButton.addEventListener('click', async () => {
    if (session) {
      session.end();
      return;
    }
    try {
      game.audio.start();
      session = await navigator.xr.requestSession('immersive-vr', {
        optionalFeatures: ['local-floor', 'bounded-floor'],
      });
      await game.renderer.xr.setSession(session);
      overlay.classList.add('hidden');
      session.addEventListener('end', () => {
        session = null;
        overlay.classList.remove('hidden');
        vrButton.textContent = 'Enter VR';
      });
      vrButton.textContent = 'Exit VR';
    } catch (err) {
      console.error(err);
      vrNote.textContent = `Could not start VR: ${err.message}`;
    }
  });

  desktopButton.addEventListener('click', () => {
    game.audio.start();
    overlay.classList.add('hidden');
    document.getElementById('desktop-help').classList.remove('hidden');
    game.keyboard.used = true;
  });

  document.getElementById('show-menu').addEventListener('click', () => {
    overlay.classList.remove('hidden');
    document.getElementById('desktop-help').classList.add('hidden');
  });

  document.body.dataset.ready = 'true';
}

boot().catch((err) => {
  console.error(err);
  const el = document.getElementById('vr-note');
  if (el) el.textContent = `Something went wrong: ${err.message}`;
});
