#include "FastLED.h"

#define NUM_LEDS (2*73+2*41)
#define LED_DATA_PIN 3
#define BRIGHTNESS 255 //range is 0..255 with 255 beeing the MAX brightness

// --------------------------------------------------------------------------------------------
// NO CHANGE REQUIRED BELOW THIS LINE
// --------------------------------------------------------------------------------------------

#define UPDATES_PER_SECOND 60  // animation and fade speed only — ambilight mode is paced by serial data
#define TIMEOUT 3000
#define MODE_ANIMATION 0
#define MODE_AMBILIGHT 1
#define MODE_BLACK 2
uint8_t mode = MODE_ANIMATION;

uint8_t currentBrightness = BRIGHTNESS;
byte MESSAGE_PREAMBLE[] = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
const uint8_t PREAMBLE_LENGTH = 10;
uint8_t current_preamble_position = 0;

unsigned long last_serial_available = 0UL;

CRGB leds[NUM_LEDS];
CRGB ledsTemp[NUM_LEDS];
byte buffer[3];

// Filler animation attributes
CRGBPalette16 currentPalette = RainbowColors_p;
TBlendType    currentBlending = LINEARBLEND;
uint8_t startIndex = 0;

void setup()
{
  Serial.begin(1000000);
  FastLED.clear(true);
  FastLED.addLeds<WS2812B, LED_DATA_PIN, GRB>(leds, NUM_LEDS);
  FastLED.setBrightness(currentBrightness);
  FastLED.setDither(0);
}

void loop()
{
  switch (mode) {
    case MODE_ANIMATION:
      fillLEDsFromPaletteColors();
      break;

    case MODE_AMBILIGHT:
      processIncomingData();
      break;

    case MODE_BLACK:
      showBlack();
      break;
  }
}
void processIncomingData()
{
  if (waitForPreamble(TIMEOUT))
  {
    // Read LED colour data — 3 bytes per LED (sent as BGR)
    for (int ledNum = 0; ledNum < NUM_LEDS; ledNum++)
    {
      if (Serial.readBytes((char*)buffer, 3) < 3) return;
      ledsTemp[ledNum] = CRGB(buffer[2], buffer[1], buffer[0]);
    }

    // Read postamble — a closing 3-byte marker; only update LEDs if it matches
    if (Serial.readBytes((char*)buffer, 3) < 3) return;
    if (buffer[0] == 85 && buffer[1] == 204 && buffer[2] == 165)
    {
      memcpy(leds, ledsTemp, sizeof(leds));

      if (currentBrightness < BRIGHTNESS)
      {
        currentBrightness++;
        FastLED.setBrightness(currentBrightness);
      }

      FastLED.show();
    }
  }
  else
  {
    // Timeout waiting for preamble — go dark
    mode = MODE_BLACK;
  }
}

bool waitForPreamble(int timeout)
{
  last_serial_available = millis();
  current_preamble_position = 0;
  while (current_preamble_position < PREAMBLE_LENGTH)
  {
    if (Serial.available() > 0)
    {
      last_serial_available = millis();

      if (Serial.read() == MESSAGE_PREAMBLE[current_preamble_position])
      {
        current_preamble_position++;
      }
      else
      {
        current_preamble_position = 0;
      }
    }

    if (millis() - last_serial_available > timeout)
    {
      return false;
    }
  }
  return true;
}

void fillLEDsFromPaletteColors()
{
  startIndex++;

  uint8_t colorIndex = startIndex;
  for ( int i = 0; i < NUM_LEDS; i++) {
    leds[i] = ColorFromPalette(currentPalette, colorIndex, BRIGHTNESS, currentBlending);
    colorIndex += 3;
  }

  FastLED.delay(1000 / UPDATES_PER_SECOND);

  if (Serial.available() > 0)
  {
    mode = MODE_AMBILIGHT;
  }
}

void showBlack()
{
  if (currentBrightness > 0)
  {
    currentBrightness--;
    FastLED.setBrightness(currentBrightness);
  }

  FastLED.delay(1000 / UPDATES_PER_SECOND);

  if (Serial.available() > 0)
  {
    mode = MODE_AMBILIGHT;
  }
}
