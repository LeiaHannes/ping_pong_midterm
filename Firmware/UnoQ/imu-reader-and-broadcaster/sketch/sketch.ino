#include <Arduino_RouterBridge.h>
#include "Arduino_LED_Matrix.h"
#include <Wire.h>
#include <math.h>
#include <string.h>

const uint8_t MPU_ADDR = 0x68;
const float ACCEL_LSB_PER_G = 4096.0f;      // ±8 g
const float GYRO_LSB_PER_DPS = 16.4f;       // ±2000 °/s
const float DEG2RAD = 0.01745329252f;

// Fusion settings
const float KP_NORMAL = 1.0f;               // how strongly gravity corrects the tilt
const float KP_STARTUP = 10.0f;             // much stronger for the first seconds, to lock on quickly
const unsigned long STARTUP_MS = 3000;
const float ACCEL_TRUST = 0.15f;            // only use the accelerometer within 15% of 1 g
const unsigned long SEND_INTERVAL_US = 10000;   // send at 100 Hz

// Status display settings
const unsigned long PY_TIMEOUT_MS = 3000;   // Python side counts as gone after this long without a heartbeat
const int ERROR_THRESHOLD = 50;             // consecutive failed sensor reads before showing an error
const unsigned long BLINK_MS = 500;

Arduino_LED_Matrix matrix;

// Orientation as a quaternion (w, x, y, z), sensor frame -> world frame
float q0 = 1.0f, q1 = 0.0f, q2 = 0.0f, q3 = 0.0f;
float gyroBias[3] = {0.0f, 0.0f, 0.0f};

unsigned long lastMicros = 0;
unsigned long lastSend = 0;
unsigned long startMillis = 0;
unsigned long lastReport = 0;
int loopCount = 0;

int consecutiveErrors = 0;
volatile unsigned long lastPyAlive = 0;     // set from the Bridge callback
const char* shownText = "";
bool blinkOn = true;
unsigned long lastBlink = 0;

// ---------- Status display ----------

const int MATRIX_W = 13;
const int MATRIX_H = 8;
uint8_t frame[MATRIX_W * MATRIX_H];         // one value per LED, row by row

// A tiny 3x5 font with just the characters the status codes need.
// Each row is 3 bits, leftmost pixel in the highest bit.
struct Glyph {
  char c;
  uint8_t rows[5];
};

const Glyph GLYPHS[] = {
  {'-', {0b000, 0b000, 0b111, 0b000, 0b000}},
  {'A', {0b010, 0b101, 0b111, 0b101, 0b101}},
  {'C', {0b111, 0b100, 0b100, 0b100, 0b111}},
  {'E', {0b111, 0b100, 0b110, 0b100, 0b111}},
  {'K', {0b101, 0b101, 0b110, 0b101, 0b101}},
  {'O', {0b111, 0b101, 0b101, 0b101, 0b111}},
  {'P', {0b110, 0b101, 0b110, 0b100, 0b100}},
  {'R', {0b110, 0b101, 0b110, 0b101, 0b101}},
  {'Y', {0b101, 0b101, 0b010, 0b010, 0b010}},
};

const uint8_t* findGlyph(char c) {
  for (const Glyph& g : GLYPHS)
    if (g.c == c) return g.rows;
  return nullptr;
}

void drawChar(char c, int x0, int y0) {
  const uint8_t* rows = findGlyph(c);
  if (rows == nullptr) return;
  for (int r = 0; r < 5; r++)
    for (int col = 0; col < 3; col++)
      if (rows[r] & (0b100 >> col))
        frame[(y0 + r) * MATRIX_W + (x0 + col)] = 1;
}

// Shows up to two characters, centered; only redraws when the text changes
void showStatus(const char* text) {
  if (strcmp(text, shownText) == 0) return;
  shownText = text;

  memset(frame, 0, sizeof(frame));
  if (text[0] != '\0') {
    drawChar(text[0], 3, 1);
    if (text[1] != '\0') drawChar(text[1], 7, 1);
  }
  matrix.draw(frame);
}

// Called by the Python side once a second
void onPyAlive() {
  lastPyAlive = millis();
}

// Chooses what to show: error, waiting for Python, or a blinking OK while everything runs
void updateStatus() {
  unsigned long now = millis();

  if (consecutiveErrors >= ERROR_THRESHOLD) {
    showStatus("ER");
    return;
  }

  bool pyAlive = lastPyAlive != 0 && now - lastPyAlive < PY_TIMEOUT_MS;
  if (!pyAlive) {
    showStatus("PY");
    return;
  }

  // Blinking shows the sketch loop itself is alive, not frozen
  if (now - lastBlink >= BLINK_MS) {
    lastBlink = now;
    blinkOn = !blinkOn;
  }
  showStatus(blinkOn ? "OK" : "");
}

// ---------- Sensor ----------

void writeReg(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  Wire.write(value);
  Wire.endTransmission();
}

int16_t readInt16() {
  uint8_t hi = Wire.read();
  uint8_t lo = Wire.read();
  return (int16_t)((hi << 8) | lo);
}

bool readMpu(int16_t accel[3], int16_t gyro[3]) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU_ADDR, (uint8_t)14) != 14) return false;

  for (int i = 0; i < 3; i++) accel[i] = readInt16();
  readInt16();                                    // temperature, unused
  for (int i = 0; i < 3; i++) gyro[i] = readInt16();
  return true;
}

// Average the gyro while the sensor is still; that average is its constant offset
void calibrateGyro() {
  Monitor.println("Hold the sensor still: measuring gyro bias...");
  const int samples = 1000;
  long sum[3] = {0, 0, 0};
  int16_t a[3], g[3];
  int got = 0;
  while (got < samples) {
    if (readMpu(a, g)) {
      for (int i = 0; i < 3; i++) sum[i] += g[i];
      got++;
    }
    delayMicroseconds(1000);
  }
  for (int i = 0; i < 3; i++) gyroBias[i] = sum[i] / (float)samples;

  Monitor.print("Gyro bias (dps): ");
  for (int i = 0; i < 3; i++) { Monitor.print(gyroBias[i] / GYRO_LSB_PER_DPS, 2); Monitor.print(" "); }
  Monitor.println();
}

// Mahony filter: follow the gyro, nudged by gravity when the accelerometer can be trusted.
// Gyro in rad/s, accel in g.
void mahonyUpdate(float gx, float gy, float gz, float ax, float ay, float az, float kp, float dt) {
  float norm = sqrtf(ax * ax + ay * ay + az * az);
  if (norm > 0.0f && fabsf(norm - 1.0f) < ACCEL_TRUST) {
    ax /= norm; ay /= norm; az /= norm;

    // Direction of gravity predicted by the current orientation, in the sensor frame
    float vx = 2.0f * (q1 * q3 - q0 * q2);
    float vy = 2.0f * (q0 * q1 + q2 * q3);
    float vz = q0 * q0 - q1 * q1 - q2 * q2 + q3 * q3;

    // Rotation that would bring the prediction toward the measurement
    float ex = ay * vz - az * vy;
    float ey = az * vx - ax * vz;
    float ez = ax * vy - ay * vx;

    gx += kp * ex;
    gy += kp * ey;
    gz += kp * ez;
  }

  // Advance the quaternion by the (corrected) rotation rate
  gx *= 0.5f * dt;
  gy *= 0.5f * dt;
  gz *= 0.5f * dt;
  float qa = q0, qb = q1, qc = q2;
  q0 += -qb * gx - qc * gy - q3 * gz;
  q1 +=  qa * gx + qc * gz - q3 * gy;
  q2 +=  qa * gy - qb * gz + q3 * gx;
  q3 +=  qa * gz + qb * gy - qc * gx;

  float inv = 1.0f / sqrtf(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
  q0 *= inv; q1 *= inv; q2 *= inv; q3 *= inv;
}

// ---------- Main ----------

void setup() {
  Bridge.begin();
  Monitor.begin();

  matrix.begin();
  matrix.setGrayscaleBits(1);   // each LED is simply on (1) or off (0)
  showStatus("--");

  Bridge.provide("py_alive", onPyAlive);

  Wire.begin();
  Wire.setClock(400000);

  writeReg(0x6B, 0x01);   // wake up, gyro clock
  writeReg(0x1A, 0x02);   // ~94 Hz low-pass
  writeReg(0x19, 0x00);   // 1 kHz sampling
  writeReg(0x1B, 0x18);   // ±2000 °/s
  writeReg(0x1C, 0x10);   // ±8 g
  delay(100);

  showStatus("CA");       // calibrating: keep the sensor still
  calibrateGyro();

  startMillis = millis();
  lastMicros = micros();
}

void loop() {
  int16_t a[3], g[3];
  if (!readMpu(a, g)) {
    if (consecutiveErrors < ERROR_THRESHOLD) consecutiveErrors++;
    updateStatus();
    return;
  }
  consecutiveErrors = 0;

  unsigned long now = micros();
  float dt = (now - lastMicros) * 1e-6f;
  lastMicros = now;
  if (dt <= 0.0f || dt > 0.1f) return;   // skip a bad interval rather than jump

  float gx = (g[0] - gyroBias[0]) / GYRO_LSB_PER_DPS * DEG2RAD;
  float gy = (g[1] - gyroBias[1]) / GYRO_LSB_PER_DPS * DEG2RAD;
  float gz = (g[2] - gyroBias[2]) / GYRO_LSB_PER_DPS * DEG2RAD;
  float ax = a[0] / ACCEL_LSB_PER_G;
  float ay = a[1] / ACCEL_LSB_PER_G;
  float az = a[2] / ACCEL_LSB_PER_G;

  float kp = (millis() - startMillis < STARTUP_MS) ? KP_STARTUP : KP_NORMAL;
  mahonyUpdate(gx, gy, gz, ax, ay, az, kp, dt);
  loopCount++;

  if (now - lastSend >= SEND_INTERVAL_US) {
    lastSend = now;
    Bridge.notify("imu_quat", q0, q1, q2, q3);
  }

  updateStatus();

  if (millis() - lastReport >= 5000) {
    lastReport = millis();
    Monitor.print("fusion Hz: ");
    Monitor.print(loopCount / 2);
    Monitor.print("   q: ");
    Monitor.print(q0, 3); Monitor.print(" ");
    Monitor.print(q1, 3); Monitor.print(" ");
    Monitor.print(q2, 3); Monitor.print(" ");
    Monitor.println(q3, 3);
    loopCount = 0;
  }
}