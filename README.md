# 🌟 StrandVenture

**StrandVenture** is an interactive Unity-based educational adventure designed to help students explore different senior high school strands through gamified storytelling and immersive 3D experiences.

> 🎓 Learn, play, and discover your future — one strand at a time.

---

## 🚀 Features

### 🧠 Dynamic Strand Experiences
Each strand offers a unique, hands-on activity that reflects real-world applications of the course:
- **ABM** (Accountancy, Business, and Management): Handle simulated financial records and categorize transactions.
- **HUMSS** (Humanities and Social Sciences): Participate in ethical decision-making scenarios.
- **STEM** (Science, Technology, Engineering, and Mathematics): Solve analytical and logical challenges.
- **ICT** (Information and Communications Technology): Engage in programming or digital literacy tasks.

### 🤖 AI-Powered Interaction
- Uses **Groq’s LLM API** to generate natural dialogues and nicknames for users.
- Includes **Groq TTS (Text-to-Speech)** integration for voice responses and dynamic narration.

### 🎬 Cinematic Flow
- Animated scene transitions and camera movement sequences.
- Floating, talking bot character that guides the player throughout the game.
- Background video looping for a polished, futuristic feel.

### 💾 Persistent Player Data
- Saves player name, nickname, and section using `PlayerPrefs`.
- Automatically personalizes dialogues and transitions between scenes.

---

## 🧩 Scenes Overview

| Scene Name | Description |
|-------------|-------------|
| **TitleScene** | Opening screen with start/agree buttons and transitions. |
| **UserInfoScene** | Collects player name and section, with AI nickname generation. |
| **ExamScene** | Interactive quiz determining strand affinity. |
| **ABMScene** | Financial categorization mini-game. |
| **HUMSSScene** | Moral choice-based ethics simulation. |
| **JobExpoScene** | Mock interview activity with AI feedback. |
| **ResultScenes** | Displays strand-based performance summaries. |

---

## 🛠️ Tech Stack

- **Engine:** Unity (2022.3 LTS or newer)
- **Language:** C#  
- **APIs:** Groq Chat Completions + Groq Text-to-Speech  
- **UI Frameworks:** TextMeshPro, Unity UI System  
- **Audio:** WAV playback via `UnityWebRequest` and custom decoding utility (`WavUtility.cs`)
