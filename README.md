# BlinkShield: Your Virtual Pet for Eye Care and Digital Balance

**BlinkShield** is a cross-platform (Mobile & Desktop) virtual pet application designed to combat Digital Eye Strain (DES) among students and professionals. By combining real-time screen time tracking, gamification, and behavioral psychology, BlinkShield encourages users to adopt healthier screen habits through the 20-20-20 rule.

> **Academic Context:** This project is a Capstone Project presented to the College of Computer and Information Sciences at the **Polytechnic University of the Philippines (PUP)** in partial fulfillment of the requirements for the degree Bachelor of Science in Information Technology (2026).

---

## 📖 Project Overview

Digital Eye Strain has become a widespread concern due to prolonged screen exposure, affecting visual comfort, productivity, and well-being. Existing eye-care apps often lack engagement and behavior-changing mechanics. 

BlinkShield fills this gap by introducing **Blinky**, a virtual pet that acts as a behavioral motivator. The pet's health and emotional state reflect the user's screen habits. By taking breaks, following the 20-20-20 rule, and playing eye-friendly mini-games, users keep Blinky happy while protecting their own eyes.

### Key Features
- **🐾 Virtual Pet System:** Interactive pet (Blinky) that reacts to the user's screen time behavior.
- **⏱️ Real-Time Screen Monitoring:** Tracks screen usage and triggers customizable reminders.
- **👁️ 20-20-20 Reminders:** Smart notifications to look 20 feet away for 20 seconds every 20 minutes.
- **🎮 Gamified Engagement:** Mini-games (Blink Challenge, Zoom & Tap), pet evolution, rewards, achievements, and a wardrobe shop.
- **📚 Eye Library:** Educational content on DES symptoms, eye care tips, and common issues.
- **📊 Health Logs & Progress:** DES surveys, weekly progress reports, and screen time history.
- **🖥️ Admin Panel:** Web-based dashboard for managing users, content, achievements, and feedback.

---

## 🛠️ Technical Architecture

The system utilizes a multi-platform architecture, combining a Unity front-end with a robust PHP/MySQL backend.

### Tech Stack
- **Front-End (App):** Unity (C#) for cross-platform mobile and desktop development.
- **Back-End (Admin & API):** PHP & MySQL (managed via phpMyAdmin).
- **Local Storage:** SQLite (for offline persistence and syncing).
- **UI/UX Design:** Canva.
- **Version Control:** Unity Version Control / GitHub.
- **IDE:** Visual Studio / Visual Studio Code.

### System Architecture
1. **Mobile/Desktop Client:** Built in Unity, handling UI, game mechanics, camera-based blink detection, and local SQLite storage.
2. **Backend Server:** PHP scripts handling authentication, data synchronization, and CRUD operations.
3. **Database:** MySQL relational database storing user profiles, screen time logs, achievements, wardrobe items, and eye health surveys.
4. **Admin Web Panel:** A web-based interface for administrators to manage content (Trivias, Tips, Issues), monitor user feedback, and track system history.

---

## 🚀 Getting Started

### Prerequisites (For Developers)
To run or build this project, you will need:
- **Unity Hub & Unity Editor** (Recommended version: Latest LTS)
- **Visual Studio** (with C# development workload)
- **XAMPP / WAMP / MAMP** (For local PHP/MySQL server)
- **phpMyAdmin** (For database management)
- **Android SDK / Xcode** (If building for mobile)

### Installation & Setup

1. **Clone the Repository:**
   ```bash
   git clone https://github.com/your-username/BlinkShield.git
