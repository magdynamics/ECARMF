import unittest
from irs_aikb.schedule_requirements import expected_schedules

class ScheduleRequirementTests(unittest.TestCase):
    def test_passthrough_expectations(self):
        names = {x["schedule"] for x in expected_schedules({"form_family": "1120-S", "flags": {}})}
        self.assertTrue({"Schedule K", "Schedule K-1", "Schedule L", "Schedule M-1", "Schedule M-2"} <= names)

    def test_individual_activity_expectations(self):
        names = {x["schedule"] for x in expected_schedules({
            "form_family": "1040", "flags": {"business_activity": True, "farm_activity": True}})}
        self.assertEqual(names, {"Schedule C", "Schedule F"})

if __name__ == "__main__":
    unittest.main()
