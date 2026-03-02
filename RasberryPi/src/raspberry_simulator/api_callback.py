"""HTTP callback utility for sending control results to API."""
import logging
import asyncio
import aiohttp
from datetime import datetime

logger = logging.getLogger(__name__)

async def send_results_to_api(api_url, line_id, voltage, speed, filtered_ct_seconds, timestamp, session=None, timeout_sec=5, max_retries=3):
    payload = {"line_id": line_id, "voltage": voltage, "speed": speed, "filtered_ct_seconds": filtered_ct_seconds, "timestamp": timestamp.isoformat()}
    should_close_session = False
    if session is None:
        session = aiohttp.ClientSession()
        should_close_session = True
    try:
        timeout = aiohttp.ClientTimeout(total=timeout_sec)
        for attempt in range(1, max_retries + 1):
            try:
                async with session.post(api_url, json=payload, timeout=timeout, headers={"Content-Type": "application/json"}) as response:
                    if response.status in (200, 201, 202):
                        logger.info("API callback successful - line=%s url=%s status=%s", line_id, api_url, response.status)
                        return True
            except asyncio.TimeoutError:
                logger.warning("API callback timeout - attempt=%d/%d", attempt, max_retries)
                if attempt < max_retries:
                    await asyncio.sleep(2 ** (attempt - 1))
            except aiohttp.ClientError as exc:
                logger.warning("API callback client error - attempt=%d/%d error=%s", attempt, max_retries, exc)
                if attempt < max_retries:
                    await asyncio.sleep(2 ** (attempt - 1))
        logger.error("API callback failed after %d attempts", max_retries)
        return False
    finally:
        if should_close_session:
            await session.close()

def send_results_to_api_sync_wrapper(api_url, line_id, voltage, speed, filtered_ct_seconds, timestamp, timeout_sec=5, max_retries=3):
    try:
        loop = asyncio.get_event_loop()
        if loop.is_closed():
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
    except RuntimeError:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
    return loop.run_until_complete(send_results_to_api(api_url, line_id, voltage, speed, filtered_ct_seconds, timestamp, timeout_sec=timeout_sec, max_retries=max_retries))
