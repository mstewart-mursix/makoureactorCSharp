Role



Add build flags and dependencies for the new feature.



Steps



CMake option:



option(MR\_ENABLE\_LLM\_GENERATOR "Enable LLM Scene Generator feature" ON)





Guard new sources:



if (MR\_ENABLE\_LLM\_GENERATOR)

&nbsp; target\_sources(MakouReactor PRIVATE

&nbsp;   src/ui/LLMSceneDialog.cpp

&nbsp;   src/ai/LLMClient.cpp

&nbsp;   src/ai/LLMSceneController.cpp

&nbsp;   src/ai/PlanParser.cpp

&nbsp;   src/ai/PlanValidator.cpp

&nbsp;   src/ai/PlanMapper.cpp

&nbsp;   src/ai/LayoutAdjuster.cpp

&nbsp; )

&nbsp; target\_compile\_definitions(MakouReactor PRIVATE MR\_ENABLE\_LLM\_GENERATOR=1)

endif()





Ensure Qt5::Network (or Qt6) is linked.



Acceptance Criteria



Build succeeds with/without the flag.



No symbol leaks when disabled.

