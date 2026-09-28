// Telegram WebApp SDK Initialization
const tg = window.Telegram?.WebApp;

// Состояние приложения
const state = {
  user: {
    id: 123456789,
    first_name: "Гость",
    last_name: "",
    username: null
  },
  vehicles: [],
  selectedVehicle: null,
  selectedDate: new Date().toISOString().split('T')[0],
  selectedSlotTime: null,
  currentTab: 'catalog'
};

// Базовый путь к API
const API_BASE = '/api';

// Инициализация
document.addEventListener('DOMContentLoaded', () => {
  initTelegramApp();
  initUser();
  initEventListeners();
  loadVehicles();
});

function initTelegramApp() {
  if (tg) {
    tg.ready();
    tg.expand();
    
    // Применение цветов темы Telegram
    if (tg.setHeaderColor) {
      tg.setHeaderColor('secondary_bg_color');
    }
    if (tg.setBackgroundColor) {
      tg.setBackgroundColor('bg_color');
    }

    // Включаем подтверждение закрытия, если открыто окно брони
    tg.enableClosingConfirmation?.();
  }
}

function initUser() {
  if (tg?.initDataUnsafe?.user) {
    state.user = tg.initDataUnsafe.user;
  }

  const greetingEl = document.getElementById('userGreeting');
  const avatarEl = document.getElementById('userAvatar');

  if (greetingEl) {
    greetingEl.textContent = state.user.first_name 
      ? `Привет, ${state.user.first_name}!` 
      : "Привет, Райдер!";
  }

  if (avatarEl && state.user.first_name) {
    avatarEl.textContent = state.user.first_name.charAt(0).toUpperCase();
  }
}

function initEventListeners() {
  // Переключение вкладок
  document.getElementById('tabCatalogBtn').addEventListener('click', () => switchTab('catalog'));
  document.getElementById('tabBookingsBtn').addEventListener('click', () => switchTab('my-bookings'));
  document.getElementById('refreshBookingsBtn').addEventListener('click', loadMyBookings);

  // Закрытие модального окна
  document.getElementById('closeModalBtn').addEventListener('click', closeBookingModal);
  document.getElementById('bookingModal').addEventListener('click', (e) => {
    if (e.target.id === 'bookingModal') closeBookingModal();
  });

  // Быстрый выбор даты
  const dateChips = document.querySelectorAll('.date-chip');
  const customDateInput = document.getElementById('customDateInput');

  // Установка сегодняшней даты в input
  const today = new Date();
  customDateInput.value = today.toISOString().split('T')[0];
  customDateInput.min = today.toISOString().split('T')[0];

  dateChips.forEach(chip => {
    chip.addEventListener('click', () => {
      dateChips.forEach(c => c.classList.remove('active'));
      chip.classList.add('active');

      const offset = parseInt(chip.dataset.offset, 10);
      const targetDate = new Date();
      targetDate.setDate(today.getDate() + offset);

      const dateStr = targetDate.toISOString().split('T')[0];
      state.selectedDate = dateStr;
      customDateInput.value = dateStr;

      triggerHaptic('selection');
      if (state.selectedVehicle) {
        loadSlots(state.selectedVehicle.id, dateStr);
      }
    });
  });

  customDateInput.addEventListener('change', (e) => {
    dateChips.forEach(c => c.classList.remove('active'));
    state.selectedDate = e.target.value;
    triggerHaptic('selection');
    if (state.selectedVehicle) {
      loadSlots(state.selectedVehicle.id, e.target.value);
    }
  });

  // Кнопка подтверждения бронирования
  document.getElementById('confirmBookingBtn').addEventListener('click', submitBooking);
  document.getElementById('closeSuccessBtn').addEventListener('click', () => {
    document.getElementById('successModal').classList.remove('active');
    switchTab('my-bookings');
  });
}

// Переключение табов
function switchTab(tabName) {
  state.currentTab = tabName;
  triggerHaptic('selection');

  document.querySelectorAll('.nav-tab').forEach(tab => {
    tab.classList.toggle('active', tab.dataset.tab === tabName);
  });

  document.getElementById('catalogTab').classList.toggle('active', tabName === 'catalog');
  document.getElementById('myBookingsTab').classList.toggle('active', tabName === 'my-bookings');

  if (tabName === 'my-bookings') {
    loadMyBookings();
  }
}

// Загрузка каталога техники
async function loadVehicles() {
  const loader = document.getElementById('catalogLoader');
  const grid = document.getElementById('vehicleGrid');
  const badge = document.getElementById('vehiclesCountBadge');

  loader.style.display = 'flex';
  grid.style.display = 'none';

  try {
    const response = await fetch(`${API_BASE}/vehicles`);
    if (!response.ok) throw new Error('Ошибка загрузки данных');

    const vehicles = await response.json();
    state.vehicles = vehicles;

    badge.textContent = `${vehicles.length} моделей`;
    renderVehicles(vehicles);

    loader.style.display = 'none';
    grid.style.display = 'flex';
  } catch (error) {
    console.error(error);
    loader.innerHTML = `<p style="color:var(--accent-red);">❌ Не удалось загрузить каталог.<br>Проверьте соединение с сервером.</p>`;
    showToast('Ошибка загрузки каталога');
  }
}

// Отрисовка карточек техники
function renderVehicles(vehicles) {
  const grid = document.getElementById('vehicleGrid');
  grid.innerHTML = '';

  vehicles.forEach(vehicle => {
    const card = document.createElement('div');
    card.className = 'vehicle-card';

    const fallbackImg = 'https://images.unsplash.com/photo-1558981403-c5f9899a28bc?auto=format&fit=crop&w=800&q=80';
    const imageUrl = vehicle.imageUrl || fallbackImg;

    card.innerHTML = `
      <div class="vehicle-image-wrapper">
        <img src="${imageUrl}" alt="${escapeHtml(vehicle.Name || vehicle.name)}" class="vehicle-image" loading="lazy">
        <span class="vehicle-status-tag">Доступен</span>
        <span class="vehicle-price-tag">${formatPrice(vehicle.price || vehicle.Price)} ₽ / час</span>
      </div>
      <div class="vehicle-info">
        <h3 class="vehicle-name">${escapeHtml(vehicle.name || vehicle.Name)}</h3>
        <p class="vehicle-description">${escapeHtml(vehicle.description || vehicle.Description)}</p>
        <button class="primary-btn" onclick="openBookingModal(${vehicle.id || vehicle.Id})">
          <span>⚡</span> Забронировать слот
        </button>
      </div>
    `;

    grid.appendChild(card);
  });
}

// Открытие модального окна бронирования
window.openBookingModal = function(vehicleId) {
  const vehicle = state.vehicles.find(v => (v.id || v.Id) === vehicleId);
  if (!vehicle) return;

  state.selectedVehicle = vehicle;
  state.selectedSlotTime = null;

  triggerHaptic('impact');

  // Заполнение данных о выбранной технике
  const vName = vehicle.name || vehicle.Name;
  const vPrice = vehicle.price || vehicle.Price;
  const vImg = vehicle.imageUrl || 'https://images.unsplash.com/photo-1558981403-c5f9899a28bc?auto=format&fit=crop&w=800&q=80';

  document.getElementById('modalVehicleName').textContent = `Бронь: ${vName}`;
  document.getElementById('summaryVehicleName').textContent = vName;
  document.getElementById('summaryVehiclePrice').textContent = `${formatPrice(vPrice)} ₽ / час`;
  document.getElementById('modalVehicleImage').src = vImg;
  document.getElementById('modalTotalPrice').textContent = `${formatPrice(vPrice)} ₽`;
  document.getElementById('bookingComment').value = '';

  // Открываем Bottom Sheet
  document.getElementById('bookingModal').classList.add('active');

  // Загружаем слоты на текущую выбранную дату
  loadSlots(vehicleId, state.selectedDate);
};

// Закрытие модального окна
function closeBookingModal() {
  document.getElementById('bookingModal').classList.remove('active');
  state.selectedVehicle = null;
  state.selectedSlotTime = null;
  triggerHaptic('selection');
}

// Загрузка доступных слотов
async function loadSlots(vehicleId, dateStr) {
  const container = document.getElementById('slotsContainer');
  container.innerHTML = '<div class="spinner-small"></div>';

  try {
    const response = await fetch(`${API_BASE}/vehicles/${vehicleId}/slots?date=${dateStr}`);
    if (!response.ok) throw new Error('Ошибка расчета слотов');

    const slots = await response.json();
    renderSlots(slots);
  } catch (error) {
    console.error(error);
    container.innerHTML = '<p style="grid-column:1/-1;font-size:12px;color:var(--hint-color);text-align:center;">Не удалось загрузить слоты на эту дату.</p>';
  }
}

// Отрисовка слотов
function renderSlots(slots) {
  const container = document.getElementById('slotsContainer');
  container.innerHTML = '';

  if (!slots || slots.length === 0) {
    container.innerHTML = '<p style="grid-column:1/-1;font-size:13px;color:var(--hint-color);text-align:center;">Нет доступных слотов на выбранную дату.</p>';
    return;
  }

  slots.forEach(slot => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'slot-btn';
    btn.textContent = slot.formattedTime || slot.FormattedTime;

    const isAvailable = slot.isAvailable ?? slot.IsAvailable;
    const slotTime = slot.slotTime || slot.SlotTime;

    if (!isAvailable) {
      btn.classList.add('disabled');
      btn.disabled = true;
      btn.title = "Время уже занято";
    } else {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.slot-btn').forEach(b => b.classList.remove('selected'));
        btn.classList.add('selected');
        state.selectedSlotTime = slotTime;
        triggerHaptic('selection');
      });
    }

    container.appendChild(btn);
  });
}

// Отправка заявки на бронирование
async function submitBooking() {
  if (!state.selectedVehicle) {
    showToast('Выберите технику для бронирования');
    return;
  }

  if (!state.selectedSlotTime) {
    showToast('Пожалуйста, выберите свободное время слота!');
    triggerHaptic('error');
    return;
  }

  const confirmBtn = document.getElementById('confirmBookingBtn');
  confirmBtn.disabled = true;
  confirmBtn.textContent = 'Оформление...';

  const payload = {
    telegramId: state.user.id,
    username: state.user.username,
    firstName: state.user.first_name,
    lastName: state.user.last_name,
    vehicleId: state.selectedVehicle.id || state.selectedVehicle.Id,
    bookingDate: state.selectedSlotTime,
    comment: document.getElementById('bookingComment').value.trim()
  };

  try {
    const response = await fetch(`${API_BASE}/bookings`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    });

    if (response.status === 409) {
      showToast('⚠️ Этот слот только что заняли. Выберите другой!');
      triggerHaptic('error');
      loadSlots(payload.vehicleId, state.selectedDate);
      return;
    }

    if (!response.ok) {
      const err = await response.json();
      throw new Error(err.error || 'Ошибка оформления заявки');
    }

    const booking = await response.json();

    // Закрываем шторку
    closeBookingModal();

    // Показываем окно успеха
    showSuccessScreen(booking);
    triggerHaptic('success');
  } catch (error) {
    console.error(error);
    showToast(`Ошибка: ${error.message}`);
    triggerHaptic('error');
  } finally {
    confirmBtn.disabled = false;
    confirmBtn.textContent = 'Забронировать слот';
  }
}

// Показ экрана успешного оформления
function showSuccessScreen(booking) {
  const successModal = document.getElementById('successModal');
  const detailsBox = document.getElementById('successDetailsBox');

  const dateObj = new Date(booking.bookingDate || booking.BookingDate);
  const formattedDate = dateObj.toLocaleString('ru-RU', {
    day: 'numeric',
    month: 'long',
    hour: '2-digit',
    minute: '2-digit'
  });

  detailsBox.innerHTML = `
    <div><b>Номер заявки:</b> #${booking.id || booking.Id}</div>
    <div><b>Техника:</b> ${escapeHtml(booking.vehicleName || booking.VehicleName || state.selectedVehicle?.name)}</div>
    <div><b>Дата и время:</b> ${formattedDate}</div>
    <div><b>Сумма к оплате:</b> ${formatPrice(booking.totalPrice || booking.TotalPrice)} ₽</div>
    <div><b>Статус:</b> 🟡 Ожидает подтверждения администратора</div>
  `;

  successModal.classList.add('active');
}

// Загрузка списка бронирований пользователя
async function loadMyBookings() {
  const loader = document.getElementById('bookingsLoader');
  const listEl = document.getElementById('bookingsList');
  const emptyEl = document.getElementById('emptyBookingsState');

  loader.style.display = 'flex';
  listEl.querySelectorAll('.booking-item').forEach(el => el.remove());
  emptyEl.style.display = 'none';

  try {
    const response = await fetch(`${API_BASE}/bookings/my?telegramId=${state.user.id}`);
    if (!response.ok) throw new Error('Ошибка загрузки истории');

    const bookings = await response.json();
    loader.style.display = 'none';

    if (bookings.length === 0) {
      emptyEl.style.display = 'block';
      return;
    }

    bookings.forEach(b => {
      const item = document.createElement('div');
      item.className = 'booking-item';

      const statusMap = {
        'Pending': { title: 'Ожидает решения', class: 'pending', icon: '⏳' },
        'Confirmed': { title: 'Подтверждена', class: 'confirmed', icon: '✅' },
        'Rejected': { title: 'Отклонена', class: 'rejected', icon: '❌' },
        'Cancelled': { title: 'Отменена', class: 'rejected', icon: '🚫' }
      };

      const st = statusMap[b.status || b.Status] || { title: b.status, class: 'pending', icon: '⏳' };
      const dateObj = new Date(b.bookingDate || b.BookingDate);
      const dateStr = dateObj.toLocaleString('ru-RU', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });

      item.innerHTML = `
        <div class="booking-item-header">
          <span class="booking-item-title">${escapeHtml(b.vehicleName || b.VehicleName)}</span>
          <span class="status-badge ${st.class}">${st.icon} ${st.title}</span>
        </div>
        <div class="booking-item-meta">
          <span>📅 ${dateStr}</span>
          <span>💰 ${formatPrice(b.totalPrice || b.TotalPrice)} ₽</span>
        </div>
        ${b.comment ? `<div style="font-size:12px;color:var(--hint-color);">💬 ${escapeHtml(b.comment)}</div>` : ''}
      `;

      listEl.appendChild(item);
    });
  } catch (error) {
    console.error(error);
    loader.style.display = 'none';
    showToast('Не удалось загрузить историю заявок');
  }
}

// Утилита вибрации (Telegram Haptic Feedback)
function triggerHaptic(type) {
  if (!tg?.HapticFeedback) return;
  try {
    switch (type) {
      case 'selection':
        tg.HapticFeedback.selectionChanged();
        break;
      case 'impact':
        tg.HapticFeedback.impactOccurred('medium');
        break;
      case 'success':
        tg.HapticFeedback.notificationOccurred('success');
        break;
      case 'error':
        tg.HapticFeedback.notificationOccurred('error');
        break;
    }
  } catch (e) {
    console.debug('Haptic error:', e);
  }
}

// Всплывающее уведомление
function showToast(message) {
  const toast = document.getElementById('toastMessage');
  toast.textContent = message;
  toast.classList.add('show');
  setTimeout(() => {
    toast.classList.remove('show');
  }, 2500);
}

// Форматирование цен
function formatPrice(val) {
  return Number(val || 0).toLocaleString('ru-RU');
}

// Защита от XSS
function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}
